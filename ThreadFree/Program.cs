using System;
using System.Text;
using System.Threading;
using PeterO.Cbor;
using Com.AugustCellars.CoAP;
using Com.AugustCellars.CoAP.DTLS;
using Com.AugustCellars.CoAP.Net;
using Com.AugustCellars.CoAP.Util;
using Com.AugustCellars.COSE;
using System.Reflection;
using ThreadFree.Lib;

namespace ThreadFree
{
    class Program
    {
        static void Main(string[] args)
        {
            Uri uriOfFirstBulb = null;
            var seconds = 1800;
            try
            {
                uriOfFirstBulb = new Uri($"coaps://{args[0]}/15001/{args[1]}");
                if (args.Length >= 3)
                    seconds = int.Parse(args[2]);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine($"{Assembly.GetExecutingAssembly().FullName} : better wakeuplight for Tradfri ");
                Console.WriteLine($"Given the ID of a RGB bulb, it will fade from dim red to bright white in ~30 minutes. ");
                Console.WriteLine($"Usage: {Assembly.GetExecutingAssembly().GetName().Name} hubip lampid [seconds] ");
                Console.WriteLine($"Example: {Assembly.GetExecutingAssembly().GetName().Name} 192.168.6.102 65537 1800");
                Environment.Exit(1);
            }

            using (var hub = new HubConnection(uriOfFirstBulb.Host))
            {
                hub.Start();
                DoWakeup(uriOfFirstBulb, hub.Client, seconds);
            }
        }

        private static void DoWakeup(Uri uriOfFirstBulb, DTLSClientEndPoint client, int seconds)
        {
            // Fade from dim red to bright white in 16 bit CIExy color space
            var max = (1 << 16) - 1;
            var startX = 0.7347 * max;
            var startY = 0.2653 * max;
            var midX = startX - (0.2 * max);
            var midY = startY + (0.2 * max);
            var endX = 0.312713 * max;
            var endY = 0.329016 * max;

            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (elapsed.Elapsed.TotalSeconds < seconds)
            {
                var t = elapsed.Elapsed.TotalSeconds / seconds;
                var brightness = (int)(t * t * 253 + 1);
                var cieX = (int)Interpolate(t, startX, midX, endX);
                var cieY = (int)Interpolate(t, startY, midY, endY);

                var request = GetRequest(client, uriOfFirstBulb);
                request.PayloadString = GetPayload(1, brightness, cieX, cieY);
                request.Send();
                var response = request.WaitForResponse();

                if (response == null)
                {
                    Console.WriteLine("Request timeout");
                }
                else
                {
                    if (response.StatusCode != StatusCode.Changed)
                        Console.WriteLine(Utils.ToString(response));
                }
                Thread.Sleep(200);
            }
        }

        private static Request GetRequest(IEndPoint client, Uri uriOfFirstBulb)
        {
            Request request = Request.NewPut();
            request.EndPoint = client;
            request.URI = uriOfFirstBulb;
            return request;
        }

        private static double Interpolate(double t, double start, double mid, double end)
        {
            var a = Interpolate(t, start, mid);
            var b = Interpolate(t, mid, end);
            return Interpolate(t, a, b);
        }

        private static double Interpolate(double t, double start, double end)
        {
            return start + t * (end - start);
        }


        private static string GetPayload(int state, int brightness, int cieX, int cieY)
        {
            return $"{{\"3311\": [{{ \"5850\":{state}, \"5851\":{brightness}, \"5709\":{cieX},\"5710\":{cieY} }} ]  }}";
        }

        private static OneKey GetKey(string applicationId, string apiKey)
        {
            var authKey = new OneKey();
            authKey.Add(CoseKeyKeys.KeyType, GeneralValues.KeyType_Octet);
            authKey.Add(CoseKeyParameterKeys.Octet_k, CBORObject.FromObject(Encoding.UTF8.GetBytes(apiKey)));
            authKey.Add(CoseKeyKeys.KeyIdentifier, CBORObject.FromObject(Encoding.UTF8.GetBytes(applicationId)));
            return authKey;
        }
    }
}
