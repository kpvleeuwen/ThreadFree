using Com.AugustCellars.CoAP;
using Com.AugustCellars.CoAP.DTLS;
using Com.AugustCellars.COSE;
using CredentialManagement;
using Newtonsoft.Json;
using PeterO.Cbor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ThreadFree.Lib
{
    public class HubConnection : IDisposable
    {
        private const string DefaultApplicationName = "ThreadFree";
        private DTLSClientEndPoint _client;

        public HubConnection(string hub) : this(hub, DefaultApplicationName)
        {
        }

        public HubConnection(string hub, string applicationName)
        {
            if (string.IsNullOrEmpty(hub))
            {
                throw new ArgumentException("Need a hub hostname", nameof(hub));
            }

            if (string.IsNullOrEmpty(applicationName))
            {
                throw new ArgumentException("ApplicationName is required", nameof(applicationName));
            }

            this.hub = hub;
            _applicationName = applicationName;
        }

        private readonly string hub;
        private string _applicationName;


        public string ApplicationName => _applicationName;

        public DTLSClientEndPoint Client => _client;

        public void Start()
        {
            var key = GetStoredKey();
            _client = new DTLSClientEndPoint(key);
            _client.Start();
        }

        private OneKey GetStoredKey()
        {
            var cred = new Credential { Target = hub };
            if (!cred.Load())
            {
                cred.Username = ApplicationName;
                cred.Password = CreateApiKey();
                cred.Save();
            }
            return GetKey(ApplicationName, cred.Password);
        }

        private string CreateApiKey()
        {
            var securityKey = GetSecurityKeyFromFrontend();
            var tempKey = GetKey("Client_identity", securityKey);
            var client = new DTLSClientEndPoint(tempKey);
            client.Start();

            Request r = Request.NewPost();
            r.URI = new Uri($"coaps://{hub}/15011/9063");
            r.SetPayload($"{{\"9090\":\"{ApplicationName}\"}}"); // escape hell; this translates to {"9090":"application"} 
            r.Send(client);
            r.WaitForResponse(2 * 1000);
            if (r.Response == null)
                throw new TimeoutException($"No reply while getting api key for {ApplicationName}");
            Dictionary<string, string> response = GetJsonResponse(r);
            string apiKey;
            if (!response.TryGetValue("9091", out apiKey))
                throw new InvalidDataException($"Expected response with key 9091, got { string.Join(",", response.Keys.ToArray())}");
            return apiKey;
        }

        private string GetSecurityKeyFromFrontend()
        {
            Console.WriteLine($"Please enter the security key (looks like kKskfSHjSkwSsdk) on the back of the hub {hub}");
            return Console.ReadLine();
        }

        private static Dictionary<string, string> GetJsonResponse(Request r)
        {
            if (r.Response.ContentType != MediaType.ApplicationJson)
                throw new InvalidDataException($"Expected {MediaType.ApplicationJson}, got {MediaType.ToString(r.Response.ContentType)}");
            string payloadString = r.Response.PayloadString;
            var response = JsonConvert.DeserializeObject<Dictionary<string, string>>(payloadString);
            return response;
        }

        private static OneKey GetKey(string applicationId, string apiKey)
        {
            var authKey = new OneKey();
            authKey.Add(CoseKeyKeys.KeyType, GeneralValues.KeyType_Octet);
            authKey.Add(CoseKeyParameterKeys.Octet_k, CBORObject.FromObject(Encoding.UTF8.GetBytes(apiKey)));
            authKey.Add(CoseKeyKeys.KeyIdentifier, CBORObject.FromObject(Encoding.UTF8.GetBytes(applicationId)));
            return authKey;
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
