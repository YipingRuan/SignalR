// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests.Common;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Redis.Tests
{
    [CollectionDefinition(Name)]
    public class EndToEndTestsCollection : ICollectionFixture<RedisServerFixture<Startup>>
    {
        public const string Name = "RedisEndToEndTests";
    }

    [Collection(EndToEndTestsCollection.Name)]
    public class RedisEndToEndTests : LoggedTest
    {
        private readonly RedisServerFixture<Startup> _serverFixture;

        public RedisEndToEndTests(RedisServerFixture<Startup> serverFixture, ITestOutputHelper output) : base(output)
        {
            if (serverFixture == null)
            {
                throw new ArgumentNullException(nameof(serverFixture));
            }

            _serverFixture = serverFixture;
        }

        [ConditionalTheory]
        [SkipIfDockerNotPresent]
        [MemberData(nameof(TransportTypesAndProtocolTypes))]
        public async Task HubConnectionCanSendAndReceiveMessages(TransportType transportType, Type protocolType)
        {
            using (StartLog(out var loggerFactory, testName:
                $"{nameof(HubConnectionCanSendAndReceiveMessages)}_{transportType.ToString()}_{protocolType.Name}"))
            {
                var connection = CreateConnection(_serverFixture.FirstServer.Url + "/echo", transportType, protocolType, loggerFactory);

                await connection.StartAsync().OrTimeout();
                var str = await connection.InvokeAsync<string>("Echo", "Hello world").OrTimeout();

                Assert.Equal("Hello world", str);

                await connection.DisposeAsync().OrTimeout();
            }
        }

        [ConditionalTheory]
        [SkipIfDockerNotPresent]
        [MemberData(nameof(TransportTypesAndProtocolTypes))]
        public async Task HubConnectionCanSendAndReceiveGroupMessages(TransportType transportType, Type protocolType)
        {
            using (StartLog(out var loggerFactory, testName:
                $"{nameof(HubConnectionCanSendAndReceiveGroupMessages)}_{transportType.ToString()}_{protocolType.Name}"))
            {
                var connection = CreateConnection(_serverFixture.FirstServer.Url + "/echo", transportType, protocolType, loggerFactory);
                var secondConnection = CreateConnection(_serverFixture.SecondServer.Url + "/echo", transportType, protocolType, loggerFactory);

                var tcs = new TaskCompletionSource<string>();
                connection.On<string>("Echo", message => tcs.TrySetResult(message));
                var tcs2 = new TaskCompletionSource<string>();
                secondConnection.On<string>("Echo", message => tcs2.TrySetResult(message));

                await secondConnection.StartAsync().OrTimeout();
                await connection.StartAsync().OrTimeout();
                await connection.InvokeAsync("EchoGroup", "Test", "Hello world").OrTimeout();

                Assert.Equal("Hello world", await tcs.Task.OrTimeout());
                Assert.Equal("Hello world", await tcs2.Task.OrTimeout());

                await connection.DisposeAsync().OrTimeout();
            }
        }

        private static HubConnection CreateConnection(string url, TransportType transportType, Type protocolType, ILoggerFactory loggerFactory)
        {
            var builder = new HubConnectionBuilder()
                .WithUrl(url)
                .WithTransport(transportType)
                .WithLoggerFactory(loggerFactory);
            if (protocolType == typeof(MessagePackHubProtocol))
            {
                builder.WithMessagePackProtocol();
            }
            else if (protocolType == typeof(JsonHubProtocol))
            {
                builder.WithJsonProtocol();
            }

            return builder.Build();
        }

        public static IEnumerable<object[]> TransportTypes
        {
            get
            {
                if (TestHelpers.IsWebSocketsSupported())
                {
                    yield return new object[] { TransportType.WebSockets };
                }
                yield return new object[] { TransportType.ServerSentEvents };
                yield return new object[] { TransportType.LongPolling };
            }
        }

        public static IEnumerable<object[]> TransportTypesAndProtocolTypes
        {
            get
            {
                foreach (var transport in TransportTypes)
                {
                    yield return new object[] { transport[0], typeof(JsonHubProtocol) };
                    yield return new object[] { transport[0], typeof(MessagePackHubProtocol) };
                }
            }
        }
    }
}
