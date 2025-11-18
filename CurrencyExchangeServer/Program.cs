using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CurrencyExchangeServer
{
    class Program
    {
        private static Dictionary<string, double> exchangeRates = new Dictionary<string, double>();
        private static Dictionary<string, ConnectionInfo> activeConnections = new Dictionary<string, ConnectionInfo>();
        private static UdpClient? udpServer;
        private static int serverPort = 8888;
        private static string logFilePath = "server_log.txt";

        static async Task Main(string[] args)
        {
            Console.WriteLine("Currency Exchange Server");
            Console.WriteLine("========================");
            
            // Load exchange rates
            LoadExchangeRates();
            
            // Start UDP server
            try
            {
                udpServer = new UdpClient(serverPort);
                Console.WriteLine($"Server started on port {serverPort}");
                Console.WriteLine("Waiting for client requests...\n");
                
                await ListenForClients();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting server: {ex.Message}");
            }
            finally
            {
                udpServer?.Close();
            }
        }

        static void LoadExchangeRates()
        {
            try
            {
                // Try multiple possible locations for exchange.txt
                string[] possiblePaths = new[]
                {
                    Path.Combine("..", "..", "..", "..", "exchange.txt"), // From bin/Debug/net8.0-windows
                    Path.Combine("..", "..", "..", "exchange.txt"),       // From bin/Debug
                    Path.Combine("..", "..", "exchange.txt"),             // From bin
                    Path.Combine("..", "exchange.txt"),                   // From CurrencyExchangeServer
                    "exchange.txt"                                        // Current directory
                };
                
                string? filePath = null;
                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        filePath = path;
                        break;
                    }
                }
                
                if (filePath == null)
                {
                    Console.WriteLine("ERROR: exchange.txt file not found!");
                    return;
                }
                
                var lines = File.ReadAllLines(filePath);
                
                // Skip header line
                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = lines[i].Split(',');
                    if (parts.Length >= 2)
                    {
                        // Parse rate: 1 USD = X EUR
                        if (double.TryParse(parts[1].Trim(), out double rate))
                        {
                            exchangeRates["USD_EUR"] = rate;
                            exchangeRates["EUR_USD"] = 1.0 / rate; // Inverse rate
                        }
                    }
                }
                
                // Use the most recent rate (last line)
                if (exchangeRates.Count > 0)
                {
                    Console.WriteLine($"Loaded exchange rates:");
                    Console.WriteLine($"  USD to EUR: {exchangeRates["USD_EUR"]}");
                    Console.WriteLine($"  EUR to USD: {exchangeRates["EUR_USD"]}\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading exchange rates: {ex.Message}");
            }
        }

        static async Task ListenForClients()
        {
            // Start background task to check for inactive connections
            _ = Task.Run(CheckInactiveConnections);
            
            while (true)
            {
                try
                {
                    var result = await udpServer!.ReceiveAsync();
                    string clientKey = $"{result.RemoteEndPoint.Address}:{result.RemoteEndPoint.Port}";
                    
                    // Log connection if new client
                    if (!activeConnections.ContainsKey(clientKey))
                    {
                        LogConnection(result.RemoteEndPoint);
                        activeConnections[clientKey] = new ConnectionInfo
                        {
                            IPAddress = result.RemoteEndPoint.Address.ToString(),
                            Port = result.RemoteEndPoint.Port,
                            ConnectTime = DateTime.Now,
                            LastActivity = DateTime.Now
                        };
                    }
                    
                    // Update last activity time
                    activeConnections[clientKey].LastActivity = DateTime.Now;
                    
                    // Process request
                    string request = Encoding.UTF8.GetString(result.Buffer);
                    Console.WriteLine($"Received from {clientKey}: {request}");
                    
                    // Check for disconnect message
                    if (request.Trim().ToUpper() == "DISCONNECT")
                    {
                        LogDisconnection(clientKey);
                        continue;
                    }
                    
                    string response = ProcessRequest(request);
                    
                    // Send response
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await udpServer.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                    Console.WriteLine($"Sent response: {response}\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling client: {ex.Message}");
                }
            }
        }

        static async Task CheckInactiveConnections()
        {
            while (true)
            {
                await Task.Delay(60000); // Check every minute
                
                var now = DateTime.Now;
                var keysToRemove = new List<string>();
                
                foreach (var kvp in activeConnections)
                {
                    // If no activity for 5 minutes, consider disconnected
                    if ((now - kvp.Value.LastActivity).TotalMinutes > 5)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var key in keysToRemove)
                {
                    LogDisconnection(key);
                }
            }
        }

        static string ProcessRequest(string request)
        {
            request = request.Trim().ToUpper();
            string[] currencies = request.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (currencies.Length != 2)
            {
                return "ERROR: Invalid request format. Expected: CURRENCY1 CURRENCY2";
            }
            
            string currency1 = currencies[0];
            string currency2 = currencies[1];
            string rateKey = $"{currency1}_{currency2}";
            
            if (exchangeRates.ContainsKey(rateKey))
            {
                double rate = exchangeRates[rateKey];
                return $"1 {currency1} = {rate:F6} {currency2}";
            }
            else
            {
                return $"ERROR: Exchange rate not found for {currency1} to {currency2}";
            }
        }

        static void LogConnection(IPEndPoint clientEndPoint)
        {
            try
            {
                string dnsName = "Unknown";
                try
                {
                    IPHostEntry hostEntry = Dns.GetHostEntry(clientEndPoint.Address);
                    dnsName = hostEntry.HostName;
                }
                catch
                {
                    // DNS lookup failed, use Unknown
                }
                
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Connection established\n" +
                                 $"  IP Address: {clientEndPoint.Address}\n" +
                                 $"  Port: {clientEndPoint.Port}\n" +
                                 $"  DNS Name: {dnsName}\n" +
                                 $"  Connect Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                 $"  ----------------------------------------\n\n";
                
                File.AppendAllText(logFilePath, logEntry);
                Console.WriteLine($"New client connected: {clientEndPoint.Address}:{clientEndPoint.Port} ({dnsName})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging connection: {ex.Message}");
            }
        }

        static void LogDisconnection(string clientKey)
        {
            try
            {
                if (activeConnections.TryGetValue(clientKey, out ConnectionInfo? info))
                {
                    string dnsName = "Unknown";
                    try
                    {
                        IPHostEntry hostEntry = Dns.GetHostEntry(info.IPAddress);
                        dnsName = hostEntry.HostName;
                    }
                    catch
                    {
                        // DNS lookup failed, use Unknown
                    }
                    
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Connection closed\n" +
                                     $"  IP Address: {info.IPAddress}\n" +
                                     $"  Port: {info.Port}\n" +
                                     $"  DNS Name: {dnsName}\n" +
                                     $"  Connect Time: {info.ConnectTime:yyyy-MM-dd HH:mm:ss}\n" +
                                     $"  Disconnect Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                     $"  Duration: {(DateTime.Now - info.ConnectTime).TotalSeconds:F2} seconds\n\n";
                    
                    File.AppendAllText(logFilePath, logEntry);
                    Console.WriteLine($"Client disconnected: {info.IPAddress}:{info.Port} ({dnsName})");
                    activeConnections.Remove(clientKey);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging disconnection: {ex.Message}");
            }
        }
    }

    class ConnectionInfo
    {
        public string IPAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public DateTime ConnectTime { get; set; }
        public DateTime LastActivity { get; set; }
    }
}

