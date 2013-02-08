// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.AspNet.SignalR.Messaging;

namespace Microsoft.AspNet.SignalR.ServiceBus
{    
    // This class provides binary stream representation of Message instances
    // and allows to get Message instances back from such stream.
    internal sealed class FastMessageSerializer
    {
        private const int DataSizeNull = -1;
        private const int MessageMarker = 0;
        private const int EndOfStreamMarker = -1;
        private const int MaxDataSizeInBytes = 256 * 1024;

        public static Stream GetStream(IEnumerable<Message> messages)
        {
            return new MessagesStream(messages);
        }

        public static Message[] GetMessages(Stream stream)
        {
            return new MessagesStreamReader(stream).ReadMessages();
        }

        [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "Do not want to alter functionality.")]
        private sealed class MessagesStreamReader
        {
            private readonly BinaryReader _reader;
            private List<Message> _messages;

            public MessagesStreamReader(Stream stream)
            {
                _reader = new BinaryReader(stream);
            }

            public Message[] ReadMessages()
            {
                if (_messages != null)
                {
                    return _messages.ToArray();
                }

                _messages = new List<Message>();

                while (true)
                {
                    int marker = _reader.ReadInt32();

                    if (marker == MessageMarker)
                    {
                        string source = GetDataItem();
                        string key = GetDataItem();
                        string value = GetDataItem();
                        string commandId = GetDataItem();
                        string waitForAcValue = GetDataItem();
                        string isAckValue = GetDataItem();
                        string filter = GetDataItem();

                        _messages.Add(
                            new Message(source, key, value)
                            {
                                CommandId = commandId,
                                WaitForAck = Boolean.Parse(waitForAcValue),
                                IsAck = Boolean.Parse(isAckValue),
                                Filter = filter
                            });
                    }
                    else if (marker == EndOfStreamMarker)
                    {
                        break;
                    }
                    else
                    {
                        throw new SerializationException(string.Format(CultureInfo.CurrentCulture, Resources.Error_MalformedDataStream));
                    }
                }

                return _messages.ToArray();
            }

            private string GetDataItem()
            {
                string data;

                int dataSize = _reader.ReadInt32();

                if (dataSize < -1)
                {
                    throw new SerializationException(string.Format(CultureInfo.CurrentCulture, Resources.Error_DataSizeSmallerThanNegOne));
                }

                if (dataSize >= MaxDataSizeInBytes)
                {
                    throw new SerializationException(string.Format(CultureInfo.CurrentCulture, Resources.Error_DataSizeTooBig));
                }

                if (dataSize == DataSizeNull)
                {
                    data = null;
                }
                else if (dataSize == 0)
                {
                    data = string.Empty;
                }
                else
                {
                    byte[] buffer = _reader.ReadBytes(dataSize);
                    data = Encoding.UTF8.GetString(buffer);
                }

                return data;
            }
        }

        private sealed class MessagesStream : Stream
        {
            private readonly IEnumerator<byte[]> _iterator;

            private byte[] _sourceBuffer;
            private int _sourceBufferPosition;

            public MessagesStream(IEnumerable<Message> messages)
            {
                _iterator = CreateIterator(messages);
            }

            public override void Flush()
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override int Read(byte[] destinationBuffer, int offset, int count)
            {
                int destinationIndex = offset;
                int totalCopied = 0;

                while (totalCopied < count)
                {
                    if (_sourceBuffer == null || _sourceBuffer.Length == _sourceBufferPosition)
                    {
                        if (!_iterator.MoveNext())
                        {
                            return totalCopied;
                        }

                        _sourceBuffer = _iterator.Current;
                        _sourceBufferPosition = 0;
                    }

                    int bytesLeftInSourceBuffer = _sourceBuffer.Length - _sourceBufferPosition;
                    int bytesLeftInDestinationBuffer = count - destinationIndex;

                    int countToCopy = Math.Min(bytesLeftInDestinationBuffer, bytesLeftInSourceBuffer);
                    Array.Copy(_sourceBuffer, _sourceBufferPosition, destinationBuffer, destinationIndex, countToCopy);

                    _sourceBufferPosition += countToCopy;
                    destinationIndex += countToCopy;
                    totalCopied += countToCopy;
                }

                return totalCopied;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override bool CanRead
            {
                get { return true; }
            }

            public override bool CanSeek
            {
                get { return false; }
            }

            public override bool CanWrite
            {
                get { return false; }
            }

            public override long Length
            {
                get { throw new NotSupportedException(); }
            }

            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            protected override void Dispose(bool disposing)
            {
                _iterator.Dispose();
                base.Dispose(disposing);
            }

            private static IEnumerator<byte[]> CreateIterator(IEnumerable<Message> messages)
            {
                foreach (Message message in messages)
                {
                    yield return BitConverter.GetBytes(MessageMarker);

                    string[] itemList = new string[] { message.Source, 
                                                       message.Key, 
                                                       message.Value, 
                                                       message.CommandId, 
                                                       message.WaitForAck.ToString(), 
                                                       message.IsAck.ToString(), 
                                                       message.Filter 
                                                     };

                    foreach (string item in itemList)
                    {
                        if (item == null)
                        {
                            yield return BitConverter.GetBytes(DataSizeNull);
                        }
                        else if (item == string.Empty)
                        {
                            yield return BitConverter.GetBytes(0);
                        }
                        else
                        {
                            byte[] buffer = Encoding.UTF8.GetBytes(item);
                            yield return BitConverter.GetBytes(buffer.Length);
                            yield return buffer;
                        }
                    }
                }

                yield return BitConverter.GetBytes(EndOfStreamMarker);
            }
        }
    }
}
