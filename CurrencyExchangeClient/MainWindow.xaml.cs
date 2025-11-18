using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;

namespace CurrencyExchangeClient
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void GetRateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GetRateButton.IsEnabled = false;
                ResultTextBox.Text = "Requesting exchange rate...";

                string serverIp = ServerIpTextBox.Text.Trim();
                if (!int.TryParse(ServerPortTextBox.Text, out int port))
                {
                    ResultTextBox.Text = "ERROR: Invalid port number";
                    GetRateButton.IsEnabled = true;
                    return;
                }

                string fromCurrency = ((System.Windows.Controls.ComboBoxItem)FromCurrencyComboBox.SelectedItem).Content.ToString() ?? "";
                string toCurrency = ((System.Windows.Controls.ComboBoxItem)ToCurrencyComboBox.SelectedItem).Content.ToString() ?? "";

                if (string.IsNullOrEmpty(fromCurrency) || string.IsNullOrEmpty(toCurrency))
                {
                    ResultTextBox.Text = "ERROR: Please select both currencies";
                    GetRateButton.IsEnabled = true;
                    return;
                }

                // Create request message
                string request = $"{fromCurrency} {toCurrency}";
                
                // Send UDP request
                using (UdpClient client = new UdpClient())
                {
                    IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), port);
                    byte[] requestBytes = Encoding.UTF8.GetBytes(request);
                    
                    await client.SendAsync(requestBytes, requestBytes.Length, serverEndPoint);
                    
                    // Receive response
                    var result = await client.ReceiveAsync();
                    string response = Encoding.UTF8.GetString(result.Buffer);
                    
                    ResultTextBox.Text = $"Request: {request}\nResponse: {response}";
                }
            }
            catch (Exception ex)
            {
                ResultTextBox.Text = $"ERROR: {ex.Message}";
            }
            finally
            {
                GetRateButton.IsEnabled = true;
            }
        }
    }
}

