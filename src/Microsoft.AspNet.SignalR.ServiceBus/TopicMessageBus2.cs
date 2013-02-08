using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Messaging;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.AspNet.SignalR.ServiceBus
{
    public class TopicMessageBus2 : ScaleoutMessageBus
    {
        private IDisposable _subscription;
        private ServiceBusConnection _connection;
        private readonly string[] _topics;

        public TopicMessageBus2(string connectionString, string topicPrefix, int numberOfTopics, IDependencyResolver resolver)
            : base(resolver)
        {
            _connection = new ServiceBusConnection(connectionString);

            _topics = Enumerable.Range(0, numberOfTopics)
                                .Select(topicIndex => topicPrefix + "_" + topicIndex)
                                .ToArray();

            _subscription = _connection.Subscribe(_topics, OnMessage);
        }


        protected override Task Send(IList<Message> messages)
        {
            var taskCompletionSource = new TaskCompletionSource<object>();

            // Group messages by source (connection id)
            var messagesBySource = messages.GroupBy(m => m.Source);

            SendImpl(messagesBySource.GetEnumerator(), taskCompletionSource);

            return taskCompletionSource.Task;
        }

        private void SendImpl(IEnumerator<IGrouping<string, Message>> enumerator, TaskCompletionSource<object> taskCompletionSource)
        {
            if (!enumerator.MoveNext())
            {
                taskCompletionSource.TrySetResult(null);
            }
            else
            {
                IGrouping<string, Message> group = enumerator.Current;

                // Get the channel index we're going to use for this message
                int index = Md5Hash.Compute32bitHashCode(group.Key) % _topics.Length;

                string topic = _topics[index];

                Stream stream = FastMessageSerializer.GetStream(group);

                // Increment the channel number
                _connection.Publish(topic, stream)
                                   .Then((enumer, tcs) => SendImpl(enumer, tcs), enumerator, taskCompletionSource)
                                   .ContinueWithNotComplete(taskCompletionSource);
            }
        }

        private void OnMessage(string topicName, IEnumerable<BrokeredMessage> messages)
        {
            foreach (var m in messages)
            {
                var internalMessages = FastMessageSerializer.GetMessages(m.GetBody<Stream>())
                                                            .ToArray();

                OnReceived(topicName, (ulong)m.SequenceNumber, internalMessages);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_subscription != null)
                {
                    _subscription.Dispose();
                }

                if (_connection != null)
                {
                    _connection.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
