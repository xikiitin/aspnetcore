// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http2Cat;
using Microsoft.AspNetCore.Server.IIS.FunctionalTests.Utilities;
using Microsoft.AspNetCore.Server.IntegrationTesting.Common;
using Microsoft.AspNetCore.Server.IntegrationTesting.IIS;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.AspNetCore.Server.IIS.FunctionalTests
{
    [Collection(PublishedSitesCollection.Name)]
    public class ResetTests : IISFunctionalTestBase
    {
        public ResetTests(PublishedSitesFixture fixture) : base(fixture)
        {
        }

        private static readonly Version Win10_Regressed_DataFrame = new Version(10, 0, 20145, 0);

        [ConditionalFact]
        [MinimumOSVersion(OperatingSystems.Windows, WindowsVersions.Win10, SkipReason = "Http2 requires Win10")]
        public async Task AppException_BeforeResponseHeaders_500()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            await new HostBuilder()
                .UseHttp2Cat(deploymentResult.ApplicationBaseUri + "AppException_BeforeResponseHeaders_500", async h2Connection =>
                {
                    await h2Connection.InitializeConnectionAsync();

                    h2Connection.Logger.LogInformation("Initialized http2 connection. Starting stream 1.");

                    await h2Connection.StartStreamAsync(1, Http2Utilities.BrowserRequestHeaders, endStream: true);

                    await h2Connection.ReceiveHeadersAsync(1, decodedHeaders =>
                    {
                        Assert.Equal("500", decodedHeaders[HeaderNames.Status]);
                    });

                    var dataFrame = await h2Connection.ReceiveFrameAsync();
                    if (Environment.OSVersion.Version >= Win10_Regressed_DataFrame)
                    {
                        // TODO: Remove when the regression is fixed.
                        // https://github.com/dotnet/aspnetcore/issues/23164#issuecomment-652646163
                        Http2Utilities.VerifyDataFrame(dataFrame, 1, endOfStream: false, length: 0);

                        dataFrame = await h2Connection.ReceiveFrameAsync();
                    }
                    Http2Utilities.VerifyDataFrame(dataFrame, 1, endOfStream: true, length: 0);

                    h2Connection.Logger.LogInformation("Connection stopped.");
                })
                .Build().RunAsync();
        }

        [ConditionalFact]
        [MinimumOSVersion(OperatingSystems.Windows, WindowsVersions.Win10, SkipReason = "Http2 requires Win10")]
        [MaximumOSVersion(OperatingSystems.Windows, WindowsVersions.Win10_20H1, SkipReason = "This is last version without custom Reset support")]
        public async Task AppException_AfterHeaders_PriorOSVersions_ResetCancel()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            await new HostBuilder()
                .UseHttp2Cat(deploymentResult.ApplicationBaseUri + "AppException_AfterHeaders_PriorOSVersions_ResetCancel", async h2Connection =>
                {
                    await h2Connection.InitializeConnectionAsync();

                    h2Connection.Logger.LogInformation("Initialized http2 connection. Starting stream 1.");

                    await h2Connection.StartStreamAsync(1, Http2Utilities.BrowserRequestHeaders, endStream: true);

                    await h2Connection.ReceiveHeadersAsync(1, decodedHeaders =>
                    {
                        Assert.Equal("200", decodedHeaders[HeaderNames.Status]);
                    });

                    var resetFrame = await h2Connection.ReceiveFrameAsync();
                    Http2Utilities.VerifyResetFrame(resetFrame, expectedStreamId: 1, Http2ErrorCode.CANCEL);

                    h2Connection.Logger.LogInformation("Connection stopped.");
                })
                .Build().RunAsync();
        }

        [ConditionalFact]
        [MinimumOSVersion(OperatingSystems.Windows, "10.0.19529", SkipReason = "Custom Reset support was added in Win10_20H2.")]
        public async Task AppException_AfterHeaders_ResetInternalError()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            await new HostBuilder()
                .UseHttp2Cat(deploymentResult.ApplicationBaseUri, async h2Connection =>
                {
                    await h2Connection.InitializeConnectionAsync();

                    h2Connection.Logger.LogInformation("Initialized http2 connection. Starting stream 1.");

                    await h2Connection.StartStreamAsync(1, Http2Utilities.BrowserRequestHeaders, endStream: true);

                    await h2Connection.ReceiveHeadersAsync(1, decodedHeaders =>
                    {
                        Assert.Equal("200", decodedHeaders[HeaderNames.Status]);
                    });

                    var frame = await h2Connection.ReceiveFrameAsync();
                    if (Environment.OSVersion.Version >= Win10_Regressed_DataFrame)
                    {
                        // TODO: Remove when the regression is fixed.
                        // https://github.com/dotnet/aspnetcore/issues/23164#issuecomment-652646163
                        Http2Utilities.VerifyDataFrame(frame, 1, endOfStream: false, length: 0);

                        frame = await h2Connection.ReceiveFrameAsync();
                    }
                    Http2Utilities.VerifyResetFrame(frame, expectedStreamId: 1, Http2ErrorCode.INTERNAL_ERROR);

                    h2Connection.Logger.LogInformation("Connection stopped.");
                })
                .Build().RunAsync();
        }

        [ConditionalFact]
        public async Task Reset_Http1_NotSupported()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using HttpClient client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version11;
            var response = await client.GetStringAsync(deploymentResult.ApplicationBaseUri);
            Assert.Equal("Hello World", response);
        }

        [ConditionalFact]
        [MinimumOSVersion(OperatingSystems.Windows, WindowsVersions.Win10, SkipReason = "Http2 requires Win10")]
        [MaximumOSVersion(OperatingSystems.Windows, WindowsVersions.Win10_20H1, SkipReason = "This is last version without Reset support")]
        public async Task Reset_PriorOSVersions_NotSupported()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using HttpClient client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version20;
            var response = await client.GetStringAsync(deploymentResult.ApplicationBaseUri);
            Assert.Equal("Hello World", response);
        }

        [ConditionalFact]
        [MinimumOSVersion(OperatingSystems.Windows, "10.0.19529", SkipReason = "Reset support was added in Win10_20H2.")]
        public async Task Reset_BeforeResponse_Resets()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            await new HostBuilder()
                .UseHttp2Cat(deploymentResult.ApplicationBaseUri, async h2Connection =>
                {
                    await h2Connection.InitializeConnectionAsync();

                    h2Connection.Logger.LogInformation("Initialized http2 connection. Starting stream 1.");

                    await h2Connection.StartStreamAsync(1, Http2Utilities.BrowserRequestHeaders, endStream: true);

                    var resetFrame = await h2Connection.ReceiveFrameAsync();
                    Http2Utilities.VerifyResetFrame(resetFrame, expectedStreamId: 1, expectedErrorCode: (Http2ErrorCode)1111);

                    // Any app errors?

                    //Assert.True(deploymentResult.HttpClient.SendAsync(""))

                    h2Connection.Logger.LogInformation("Connection stopped.");
                })
                .Build().RunAsync();
        }

        [ConditionalFact]
        [MinimumOSVersion(OperatingSystems.Windows, "10.0.19529", SkipReason = "Reset support was added in Win10_20H2.")]
        public async Task Reset_AfterResponseHeaders_Resets()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            await new HostBuilder()
                .UseHttp2Cat(deploymentResult.ApplicationBaseUri, async h2Connection =>
                {
                    await h2Connection.InitializeConnectionAsync();

                    h2Connection.Logger.LogInformation("Initialized http2 connection. Starting stream 1.");

                    await h2Connection.StartStreamAsync(1, Http2Utilities.BrowserRequestHeaders, endStream: true);

                    // Any app errors?
                    //Assert.Equal(0, await appResult.Task.DefaultTimeout());

                    await h2Connection.ReceiveHeadersAsync(1, decodedHeaders =>
                    {
                        Assert.Equal("200", decodedHeaders[HeaderNames.Status]);
                    });

                    var dataFrame = await h2Connection.ReceiveFrameAsync();
                    Http2Utilities.VerifyDataFrame(dataFrame, expectedStreamId: 1, endOfStream: false, length: 0);

                    var resetFrame = await h2Connection.ReceiveFrameAsync();
                    Http2Utilities.VerifyResetFrame(resetFrame, expectedStreamId: 1, expectedErrorCode: (Http2ErrorCode)1111);

                    h2Connection.Logger.LogInformation("Connection stopped.");
                })
                .Build().RunAsync();
        }

        [ConditionalFact]
        [MinimumOSVersion(OperatingSystems.Windows, "10.0.19529", SkipReason = "Reset support was added in Win10_20H2.")]
        public async Task Reset_DurringResponseBody_Resets()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            await new HostBuilder()
                .UseHttp2Cat(deploymentResult.ApplicationBaseUri, async h2Connection =>
                {
                    await h2Connection.InitializeConnectionAsync();

                    h2Connection.Logger.LogInformation("Initialized http2 connection. Starting stream 1.");

                    await h2Connection.StartStreamAsync(1, Http2Utilities.BrowserRequestHeaders, endStream: true);

                    // Any app errors?
                    //Assert.Equal(0, await appResult.Task.DefaultTimeout());

                    await h2Connection.ReceiveHeadersAsync(1, decodedHeaders =>
                    {
                        Assert.Equal("200", decodedHeaders[HeaderNames.Status]);
                    });

                    var dataFrame = await h2Connection.ReceiveFrameAsync();
                    Http2Utilities.VerifyDataFrame(dataFrame, 1, endOfStream: false, length: 11);

                    var resetFrame = await h2Connection.ReceiveFrameAsync();
                    Http2Utilities.VerifyResetFrame(resetFrame, expectedStreamId: 1, expectedErrorCode: (Http2ErrorCode)1111);

                    h2Connection.Logger.LogInformation("Connection stopped.");
                })
                .Build().RunAsync();
        }

        
        [ConditionalFact]
        [MinimumOSVersion(OperatingSystems.Windows, "10.0.19529", SkipReason = "Reset support was added in Win10_20H2.")]
        public async Task Reset_BeforeRequestBody_Resets()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            await new HostBuilder()
                .UseHttp2Cat(deploymentResult.ApplicationBaseUri, async h2Connection =>
                {
                    await h2Connection.InitializeConnectionAsync();

                    h2Connection.Logger.LogInformation("Initialized http2 connection. Starting stream 1.");

                    await h2Connection.StartStreamAsync(1, Http2Utilities.PostRequestHeaders, endStream: false);

                    // Any app errors?
                    //Assert.Equal(0, await appResult.Task.DefaultTimeout());

                    var resetFrame = await h2Connection.ReceiveFrameAsync();
                    Http2Utilities.VerifyResetFrame(resetFrame, expectedStreamId: 1, expectedErrorCode: (Http2ErrorCode)1111);

                    h2Connection.Logger.LogInformation("Connection stopped.");
                })
                .Build().RunAsync();
        }

        [ConditionalFact]
        [MinimumOSVersion(OperatingSystems.Windows, "10.0.19529", SkipReason = "Reset support was added in Win10_20H2.")]
        public async Task Reset_DurringRequestBody_Resets()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            await new HostBuilder()
                .UseHttp2Cat(deploymentResult.ApplicationBaseUri, async h2Connection =>
                {
                    await h2Connection.InitializeConnectionAsync();

                    h2Connection.Logger.LogInformation("Initialized http2 connection. Starting stream 1.");

                    await h2Connection.StartStreamAsync(1, Http2Utilities.PostRequestHeaders, endStream: false);
                    await h2Connection.SendDataAsync(1, new byte[10], endStream: false);

                    // Any app errors?
                    //Assert.Equal(0, await appResult.Task.DefaultTimeout());

                    var resetFrame = await h2Connection.ReceiveFrameAsync();
                    Http2Utilities.VerifyResetFrame(resetFrame, expectedStreamId: 1, expectedErrorCode: (Http2ErrorCode)1111);

                    h2Connection.Logger.LogInformation("Connection stopped.");
                })
                .Build().RunAsync();
        }

        [ConditionalFact]
        [MinimumOSVersion(OperatingSystems.Windows, "10.0.19529", SkipReason = "Reset support was added in Win10_20H2.")]
        public async Task Reset_CompleteAsyncDurringRequestBody_Resets()
        {
            var deploymentParameters = GetHttpsDeploymentParameters();
            var deploymentResult = await DeployAsync(deploymentParameters);

            await new HostBuilder()
                .UseHttp2Cat(deploymentResult.ApplicationBaseUri, async h2Connection =>
                {
                    await h2Connection.InitializeConnectionAsync();

                    h2Connection.Logger.LogInformation("Initialized http2 connection. Starting stream 1.");

                    await h2Connection.StartStreamAsync(1, Http2Utilities.PostRequestHeaders, endStream: false);
                    await h2Connection.SendDataAsync(1, new byte[10], endStream: false);

                    // Any app errors?
                    //Assert.Equal(0, await appResult.Task.DefaultTimeout());

                    await h2Connection.ReceiveHeadersAsync(1, decodedHeaders =>
                    {
                        Assert.Equal("200", decodedHeaders[HeaderNames.Status]);
                    });

                    var dataFrame = await h2Connection.ReceiveFrameAsync();
                    if (Environment.OSVersion.Version >= Win10_Regressed_DataFrame)
                    {
                        // TODO: Remove when the regression is fixed.
                        // https://github.com/dotnet/aspnetcore/issues/23164#issuecomment-652646163
                        Http2Utilities.VerifyDataFrame(dataFrame, 1, endOfStream: false, length: 0);

                        dataFrame = await h2Connection.ReceiveFrameAsync();
                    }
                    Http2Utilities.VerifyDataFrame(dataFrame, 1, endOfStream: true, length: 0);

                    var resetFrame = await h2Connection.ReceiveFrameAsync();
                    Http2Utilities.VerifyResetFrame(resetFrame, expectedStreamId: 1, expectedErrorCode: Http2ErrorCode.NO_ERROR);

                    h2Connection.Logger.LogInformation("Connection stopped.");
                })
                .Build().RunAsync();
        }

        private IISDeploymentParameters GetHttpsDeploymentParameters()
        {
            var port = TestPortHelper.GetNextSSLPort();
            var deploymentParameters = Fixture.GetBaseDeploymentParameters();
            deploymentParameters.ApplicationBaseUriHint = $"https://localhost:{port}/";
            deploymentParameters.AddHttpsToServerConfig();
            return deploymentParameters;
        }
    }
}
