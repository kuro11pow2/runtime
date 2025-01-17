// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.IO.Tests;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Linq;
using Xunit;

namespace System.Net.Security.Tests
{
    using Configuration = System.Net.Test.Common.Configuration;

    public class SslStreamCertificateTrustTest
    {
        [Fact]
        // not supported on Windows, not implemented elsewhere
        [PlatformSpecific(TestPlatforms.Linux | TestPlatforms.OSX)]
        public async Task SslStream_SendCertificateTrust_CertificateCollection()
        {
            (X509Certificate2 certificate, X509Certificate2Collection caCerts) = TestHelper.GenerateCertificates(nameof(SslStream_SendCertificateTrust_CertificateCollection));

            SslCertificateTrust trust = SslCertificateTrust.CreateForX509Collection(caCerts, sendTrustInHandshake: true);
            string[] acceptableIssuers = await ConnectAndGatherAcceptableIssuers(trust);

            Assert.Equal(caCerts.Count, acceptableIssuers.Length);
            Assert.Equal(caCerts.Select(c => c.Subject), acceptableIssuers);
        }

        [Fact]
        [ActiveIssue("https://github.com/dotnet/runtime/issues/65515", TestPlatforms.Windows)]
        [PlatformSpecific(TestPlatforms.Windows | TestPlatforms.Linux | TestPlatforms.OSX)]
        public async Task SslStream_SendCertificateTrust_CertificateStore()
        {
            using X509Store store = new X509Store("Root", StoreLocation.LocalMachine);

            SslCertificateTrust trust = SslCertificateTrust.CreateForX509Store(store, sendTrustInHandshake: true);
            string[] acceptableIssuers = await ConnectAndGatherAcceptableIssuers(trust);

            // don't assert individual ellements, just that some issuers were sent
            // we use Root cert store which should always contain at least some certs
            Assert.NotEmpty(acceptableIssuers);
        }

        private async Task<string[]> ConnectAndGatherAcceptableIssuers(SslCertificateTrust trust)
        {
            (SslStream client, SslStream server) = TestHelper.GetConnectedSslStreams();
            using (client)
            using (server)
            using (X509Certificate2 serverCertificate = Configuration.Certificates.GetServerCertificate())
            using (X509Certificate2 clientCertificate = Configuration.Certificates.GetClientCertificate())
            {
                SslServerAuthenticationOptions serverOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    ClientCertificateRequired = true,
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
                    ServerCertificateContext = SslStreamCertificateContext.Create(serverCertificate, null, false, trust)
                };

                string[] acceptableIssuers = Array.Empty<string>();
                SslClientAuthenticationOptions clientOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost",
                    // Force Tls 1.2 to avoid issues with certain OpenSSL versions and Tls 1.3
                    // https://github.com/openssl/openssl/issues/7384
                    EnabledSslProtocols = SslProtocols.Tls12,
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
                    LocalCertificateSelectionCallback = (sender, targetHost, localCertificates, remoteCertificate, issuers) =>
                    {
                        if (remoteCertificate == null)
                        {
                            // ignore the first call that is called before handshake
                            return null;
                        }

                        acceptableIssuers = issuers;
                        return clientCertificate;
                    },

                };

                await TestConfiguration.WhenAllOrAnyFailedWithTimeout(
                                client.AuthenticateAsClientAsync(clientOptions),
                                server.AuthenticateAsServerAsync(serverOptions));

                return acceptableIssuers;
            }
        }
    }
}