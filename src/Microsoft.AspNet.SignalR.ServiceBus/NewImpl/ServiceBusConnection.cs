using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.ServiceBus.Infrastructure;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.AspNet.SignalR.ServiceBus
{
    public class ServiceBusConnection : IDisposable
    {
        private const int ReceiveBatchSize = 1000;

        private readonly NamespaceManager _namespaceManager;
        private readonly MessagingFactory _factory;
        private readonly string _connectionString;

        public ServiceBusConnection(string connectionString)
        {
            _connectionString = connectionString;
            _namespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);
            _factory = MessagingFactory.CreateFromConnectionString(connectionString);
        }

        public IDisposable Subscribe(IList<string> topicNames, Action<string, IEnumerable<BrokeredMessage>> handler)
        {
            var subscription = new Subscription(_namespaceManager);

            foreach (var topicPath in topicNames)
            {
                if (!_namespaceManager.TopicExists(topicPath))
                {
                    _namespaceManager.CreateTopic(topicPath);
                }

                // Create a random subscription
                string subName = Guid.NewGuid().ToString();
                SubscriptionDescription topicSubscription = _namespaceManager.CreateSubscription(topicPath, subName);
                string subscriptionEntityPath = SubscriptionClient.FormatSubscriptionPath(topicPath, subName);

                MessageReceiver receiver = _factory.CreateMessageReceiver(subscriptionEntityPath);

                subscription.TopicPath = topicPath;
                subscription.Name = subName;
                subscription.Receivers.Add(receiver);

                ReceiveMessages(topicPath, receiver, handler);
            }

            return subscription;
        }

        public Task Publish(string topicName, Stream stream)
        {
            var client = TopicClient.CreateFromConnectionString(_connectionString, topicName);
            var message = new BrokeredMessage(stream, ownsStream: true)
            {
                TimeToLive = TimeSpan.FromMinutes(1)
            };

            return client.SendAsync(message);
        }

        public void Dispose()
        {
            // REVIEW: What do we dipose here
        }

        private void ReceiveMessages(string topicPath, MessageReceiver receiver, Action<string, IEnumerable<BrokeredMessage>> handler)
        {
        receive:

            IAsyncResult result = null;

            try
            {
                result = receiver.BeginReceiveBatch(ReceiveBatchSize, ar =>
                {
                    bool backOff = false;

                    try
                    {
                        if (ar.CompletedSynchronously)
                        {
                            return;
                        }

                        handler(topicPath, receiver.EndReceiveBatch(ar));
                    }
                    catch (ServerBusyException)
                    {
                        // Too busy so back off   
                        backOff = true;
                    }
                    catch (OperationCanceledException)
                    {
                        // Closed
                        return;
                    }

                    if (backOff)
                    {
                        TaskAsyncHelper.Delay(TimeSpan.FromSeconds(20))
                                       .Then(() => ReceiveMessages(topicPath, receiver, handler));
                    }
                    else
                    {
                        ReceiveMessages(topicPath, receiver, handler);
                    }
                },
                null);
            }
            catch (OperationCanceledException)
            {
                // Closed
                return;
            }

            if (result.CompletedSynchronously)
            {
                handler(topicPath, receiver.EndReceiveBatch(result));
                goto receive;
            }
        }

        private class Subscription : IDisposable
        {
            private readonly NamespaceManager _namespaceManager;

            public Subscription(NamespaceManager namespaceManager)
            {
                _namespaceManager = namespaceManager;
                Receivers = new List<MessageReceiver>();
            }

            public string TopicPath { get; set; }
            public string Name { get; set; }
            public List<MessageReceiver> Receivers { get; private set; }

            public void Dispose()
            {
                foreach (var receiver in Receivers)
                {
                    receiver.Close();
                }

                _namespaceManager.DeleteSubscription(TopicPath, Name);
            }
        }
    }
}
