using DarkLight.CoverCalibrator;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

internal static class Program
{
    private static int Main()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new List<string>();
        Exception serverError = null;

        var server = new Thread(() =>
        {
            try
            {
                using (var client = listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                {
                    var command = new StringBuilder();
                    bool inCommand = false;
                    while (received.Count < 2)
                    {
                        int value = stream.ReadByte();
                        if (value < 0) break;
                        char c = (char)value;
                        if (c == '<')
                        {
                            command.Clear();
                            inCommand = true;
                        }
                        else if (c == '>' && inCommand)
                        {
                            string text = command.ToString();
                            received.Add(text);
                            byte[] response = Encoding.ASCII.GetBytes(text == "Z" ? "<?>\r\n" : "<1>\r\n");
                            stream.Write(response, 0, response.Length);
                            inCommand = false;
                        }
                        else if (inCommand)
                        {
                            command.Append(c);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                serverError = ex;
            }
        });
        server.Start();

        try
        {
            using (var device = new DeviceTcp())
            {
                device.Open("127.0.0.1", port);
                Assert(device.Handshake(), "TCP handshake failed");
                Assert(device.SendCommand("P") == "1", "TCP status response failed");
            }
            server.Join(3000);
            if (serverError != null) throw serverError;
            Assert(received.Count == 2 && received[0] == "Z" && received[1] == "P", "TCP command framing failed");
            Console.WriteLine("PASS: DeviceTcp handshake, framing, and response parsing");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
