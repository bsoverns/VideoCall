using AForge.Video;
using AForge.Video.DirectShow;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace VideoCall
{
    public partial class MainWindow : Window
    {
        private bool isConnected = false;
        private bool isMuted = false;
        private bool isVideoOn = true;
        private TcpListener server = null;
        private List<TcpClient> clients = new List<TcpClient>();
        private TcpClient client = null;
        private VideoCaptureDevice videoSource;
        private System.Drawing.Bitmap latestFrame;
        private readonly object frameLock = new object();

        public MainWindow()
        {
            InitializeComponent();
            UpdateConnectionStatus();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            // Connect to server
            try
            {
                string serverIp = ServerIPTextBox.Text;
                if (string.IsNullOrWhiteSpace(serverIp))
                {
                    MessageBox.Show("Please enter a valid IP address.");
                    return;
                }

                client = new TcpClient(serverIp, 12345); // Default port 12345
                isConnected = true;
                UpdateConnectionStatus();
                Task.Run(() => ReceiveFrames());
                Task.Run(() => SendFrames(client));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect: {ex.Message}");
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            // Disconnect
            client?.Close();
            isConnected = false;
            UpdateConnectionStatus();
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle mute
            isMuted = !isMuted;
            MuteButton.Content = isMuted ? "Unmute" : "Mute";
        }

        private void VideoButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle video
            isVideoOn = !isVideoOn;
            VideoButton.Content = isVideoOn ? "Video Off" : "Video On";

            if (isVideoOn)
            {
                StartVideo();
            }

            else
            {
                StopVideo();
            }
        }

        private void SendChatButton_Click(object sender, RoutedEventArgs e)
        {
            // Add chat message to the chat box
            if (!string.IsNullOrWhiteSpace(ChatInput.Text))
            {
                ChatMessages.Children.Add(new TextBlock { Text = ChatInput.Text });
                ChatInput.Clear();
            }
        }

        private void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Start server
            try
            {
                var localIP = GetLocalIPAddress();
                //server = new TcpListener(localIP, 12345); // Default port 12345
                server = new TcpListener(IPAddress.Any, 12345);
                server.Start();
                Task.Run(() => ListenForClients());
                StartServerButton.IsEnabled = false;
                StopServerButton.IsEnabled = true;
                ServerIPTextBox.Text = localIP.ToString();
                MessageBox.Show($"Server started at IP: {localIP}");
            }

            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start server: {ex.Message}");
            }
        }

        private void StopServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Stop server
            try
            {
                server.Stop();
                server = null;
                StartServerButton.IsEnabled = true;
                StopServerButton.IsEnabled = false;
                ServerIPTextBox.Clear();
                clients.ForEach(c => c.Close());
                clients.Clear();
                MessageBox.Show("Server stopped.");
            }

            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop server: {ex.Message}");
            }
        }

        private async Task ListenForClients()
        {
            while (server != null)
            {
                try
                {
                    TcpClient newClient = await server.AcceptTcpClientAsync();
                    clients.Add(newClient);
                    MessageBox.Show("Client connected.");
                    Task.Run(() => ReceiveFramesFromClient(newClient));
                }

                catch (Exception ex)
                {
                    if (server != null)
                    {
                        MessageBox.Show($"Error accepting client: {ex.Message}");
                    }
                    break;
                }
            }
        }

        private void UpdateConnectionStatus()
        {
            ConnectionStatus.Text = isConnected ? "Connected" : "Not Connected";
            ConnectButton.IsEnabled = !isConnected;
            DisconnectButton.IsEnabled = isConnected;
        }

        private IPAddress GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());

            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip;
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

        //private void StartVideo()
        private async void StartVideo()
        {
            try
            {
                // Get video devices
                var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

                if (videoDevices.Count == 0)
                {
                    MessageBox.Show("No video devices found.");
                    return;
                }

                // Diagnostic: log camera devices found
                for (int i = 0; i < videoDevices.Count; i++)
                {
                    Console.WriteLine($"Found video device: {videoDevices[i].Name}");
                }

                // Select the first video device
                videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
                videoSource.NewFrame += VideoSource_NewFrame;

                videoSource.VideoSourceError += (s, args) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Camera error: {args.Description}");
                    });
                };


                videoSource.Start();

                await Task.Delay(500); // Give the source time to initialize

                if (!videoSource.IsRunning)
                {
                    MessageBox.Show("Camera failed to start.");
                }


                //MessageBox.Show("Video started.");
            }

            catch (Exception ex)
            {
                MessageBox.Show($"Error starting video: {ex.Message}");
            }
        }

        private void StopVideo()
        {
            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.SignalToStop();
                videoSource.NewFrame -= VideoSource_NewFrame;
            }
        }

        //private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        //{
        //    lock (frameLock)
        //    {
        //        try
        //        {
        //            // Store the latest frame
        //            if (latestFrame != null)
        //            {
        //                latestFrame.Dispose();
        //            }
        //            latestFrame = (System.Drawing.Bitmap)eventArgs.Frame.Clone();

        //            // Display the image locally
        //            Dispatcher.Invoke(() =>
        //            {
        //                LocalVideoImage.Source = BitmapToImageSource(latestFrame);
        //            });

        //            // Broadcast the frame to all connected clients
        //            byte[] frameData = BitmapToByteArray(latestFrame);
        //            BroadcastFrameToClients(frameData);
        //        }
        //        catch (Exception ex)
        //        {
        //            Dispatcher.Invoke(() =>
        //            {
        //                MessageBox.Show($"Error capturing frame: {ex.Message}");
        //            });
        //        }
        //    }
        //}

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            lock (frameLock)
            {
                try
                {
                    // Clone the frame before disposing of the old one
                    var newFrame = (System.Drawing.Bitmap)eventArgs.Frame.Clone();

                    // Swap the latest frame safely
                    var oldFrame = latestFrame;
                    latestFrame = newFrame;

                    // Dispose of the old frame AFTER replacing it
                    oldFrame?.Dispose();

                    // Display the image locally
                    Dispatcher.Invoke(() =>
                    {
                        LocalVideoImage.Source = BitmapToImageSource(latestFrame);
                    });

                    // Send the frame to clients
                    byte[] frameData = BitmapToByteArray(latestFrame);
                    BroadcastFrameToClients(frameData);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Error capturing frame: {ex.Message}");
                    });
                }
            }
        }

        //private byte[] BitmapToByteArray(System.Drawing.Bitmap bitmap)
        //{
        //    using (var memory = new MemoryStream())
        //    {
        //        bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
        //        return memory.ToArray();
        //    }
        //}

        private byte[] BitmapToByteArray(System.Drawing.Bitmap bitmap)
        {
            using (var memory = new MemoryStream())
            {
                using (var tempBitmap = new System.Drawing.Bitmap(bitmap)) // Create a copy to avoid access conflicts
                {
                    tempBitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                }
                return memory.ToArray();
            }
        }

        /*
        private void BroadcastFrameToClients(byte[] frameData)
        {
            byte[] frameSize = BitConverter.GetBytes(frameData.Length);
            byte[] dataToSend = frameSize.Concat(frameData).ToArray();

            foreach (var client in clients.ToList())
            {
                if (client.Connected)
                {
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        stream.Write(dataToSend, 0, dataToSend.Length);
                        Console.WriteLine("Frame sent to client.");
                    }
                    catch (Exception ex)
                    {
                        clients.Remove(client);
                        Dispatcher.Invoke(() => MessageBox.Show($"Error broadcasting frame to client: {ex.Message}"));
                    }
                }
                else
                {
                    clients.Remove(client);
                }
            }
        }
        */

        public void BroadcastFrameToClients(byte[] frameBytes)
        {
            List<TcpClient> clientsToRemove = new List<TcpClient>();
            // Iterate through clients list
            foreach (TcpClient client in clients)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    stream.Write(frameBytes, 0, frameBytes.Length);
                }
                catch (IOException ex)
                {
                    // Mark this client for removal
                    clientsToRemove.Add(client);
                    // Optionally log the disconnect event
                    Console.WriteLine($"Removing client: {ex.Message}");
                }
            }
            // Remove and dispose of any clients that disconnected
            foreach (TcpClient client in clientsToRemove)
            {
                client.Close();
                clients.Remove(client);
            }
        }

        private void ReceiveFrames()
        {
            Task.Run(() =>
            {
                try
                {
                    while (client != null && client.Connected)
                    {
                        NetworkStream stream = client.GetStream();
                        byte[] frameSizeBytes = new byte[4];
                        int bytesRead = stream.Read(frameSizeBytes, 0, frameSizeBytes.Length);
                        if (bytesRead != frameSizeBytes.Length)
                        {
                            continue;
                        }

                        int frameSize = BitConverter.ToInt32(frameSizeBytes, 0);
                        byte[] frameData = new byte[frameSize];
                        int totalBytesRead = 0;
                        while (totalBytesRead < frameSize)
                        {
                            bytesRead = stream.Read(frameData, totalBytesRead, frameSize - totalBytesRead);
                            if (bytesRead == 0)
                            {
                                break;
                            }
                            totalBytesRead += bytesRead;
                        }

                        if (totalBytesRead == frameSize)
                        {
                            Console.WriteLine("Received frame data from server.");
                            using (var bitmap = ByteArrayToBitmap(frameData))
                            {
                                BitmapImage bitmapImage = BitmapToImageSource(bitmap);

                                Dispatcher.Invoke(() =>
                                {
                                    // Display the video from the other party
                                    MainVideoGrid.Children.Clear();
                                    MainVideoGrid.Children.Add(new System.Windows.Controls.Image { Source = bitmapImage, Stretch = System.Windows.Media.Stretch.Fill });
                                    Console.WriteLine("Frame displayed.");
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Error receiving frame: {ex.Message}");
                    });
                }
            });
        }

        //private async Task ReceiveFramesFromClient(TcpClient client)
        //{
        //    try
        //    {
        //        while (client != null && client.Connected)
        //        {
        //            NetworkStream stream = client.GetStream();
        //            byte[] frameSizeBytes = new byte[4];
        //            int bytesRead = await stream.ReadAsync(frameSizeBytes, 0, frameSizeBytes.Length);
        //            if (bytesRead != frameSizeBytes.Length)
        //            {
        //                continue;
        //            }

        //            int frameSize = BitConverter.ToInt32(frameSizeBytes, 0);
        //            byte[] frameData = new byte[frameSize];
        //            int totalBytesRead = 0;
        //            while (totalBytesRead < frameSize)
        //            {
        //                bytesRead = await stream.ReadAsync(frameData, totalBytesRead, frameSize - totalBytesRead);
        //                if (bytesRead == 0)
        //                {
        //                    break;
        //                }
        //                totalBytesRead += bytesRead;
        //            }

        //            if (totalBytesRead == frameSize)
        //            {
        //                Console.WriteLine("Received frame data from client.");
        //                using (var bitmap = ByteArrayToBitmap(frameData))
        //                {
        //                    BitmapImage bitmapImage = BitmapToImageSource(bitmap);

        //                    Dispatcher.BeginInvoke(new Action(() =>
        //                    {
        //                        // Display the video from the client
        //                        MainVideoImage.Source = bitmapImage;
        //                        Console.WriteLine("Frame displayed.");
        //                    }));

        //                    // Broadcast received frame to other clients, excluding the one it came from
        //                    BroadcastFrameToClients(frameData, client);
        //                }
        //            }
        //        }
        //    }

        //    catch (Exception ex)
        //    {
        //        Dispatcher.BeginInvoke(new Action(() =>
        //        {
        //            MessageBox.Show($"Error receiving frame from client: {ex.Message}");
        //        }));
        //    }
        //}

        private async Task ReceiveFramesFromClient(TcpClient client)
        {
            try
            {
                while (client != null && client.Connected)
                {
                    NetworkStream stream = client.GetStream();
                    byte[] frameSizeBytes = new byte[4];
                    int bytesRead = await stream.ReadAsync(frameSizeBytes, 0, frameSizeBytes.Length);
                    if (bytesRead != frameSizeBytes.Length) continue;

                    //int frameSize = BitConverter.ToInt32(frameSizeBytes, 0);
                    int frameSize = BitConverter.ToInt32(frameSizeBytes, 0);
                    if (frameSize <= 0 || frameSize > 10_000_000) // ~10MB sanity cap
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Invalid frame size: {frameSize} — possible corruption or overflow.");
                        });
                        break;
                    }

                    byte[] frameData = new byte[frameSize];
                    int totalBytesRead = 0;

                    while (totalBytesRead < frameSize)
                    {
                        bytesRead = await stream.ReadAsync(frameData, totalBytesRead, frameSize - totalBytesRead);
                        if (bytesRead == 0) break;
                        totalBytesRead += bytesRead;
                    }

                    if (totalBytesRead == frameSize)
                    {
                        Console.WriteLine("Received frame data from client.");

                        using (var bitmap = ByteArrayToBitmap(frameData))
                        {
                            BitmapImage bitmapImage = BitmapToImageSource(bitmap);
                            bitmapImage.Freeze(); // Ensure it can be accessed across threads

                            Dispatcher.Invoke(() =>
                            {
                                MainVideoImage.Source = bitmapImage;
                                Console.WriteLine("Frame displayed.");
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show($"Error receiving frame from client: {ex.Message}"));
            }
        }

        /*
        private void BroadcastFrameToClients(byte[] frameData, TcpClient senderClient)
        {
            byte[] frameSize = BitConverter.GetBytes(frameData.Length);
            byte[] dataToSend = frameSize.Concat(frameData).ToArray();

            foreach (var client in clients.ToList())
            {
                if (client.Connected && client != senderClient)
                {
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        stream.Write(dataToSend, 0, dataToSend.Length);
                        Console.WriteLine("Frame sent to client.");
                    }
                    catch (Exception ex)
                    {
                        clients.Remove(client);
                        Dispatcher.BeginInvoke(new Action(() => MessageBox.Show($"Error broadcasting frame to client: {ex.Message}")));
                    }
                }

                else
                {
                    clients.Remove(client);
                }
            }
        }
        */

        private System.Drawing.Bitmap ByteArrayToBitmap(byte[] byteArray)
        {
            using (var memoryStream = new MemoryStream(byteArray))
            {
                return new System.Drawing.Bitmap(memoryStream);
            }
        }

        //private BitmapImage BitmapToImageSource(System.Drawing.Bitmap bitmap)
        //{
        //    using (var memory = new MemoryStream())
        //    {
        //        bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
        //        memory.Position = 0;
        //        BitmapImage bitmapImage = new BitmapImage();
        //        bitmapImage.BeginInit();
        //        bitmapImage.StreamSource = memory;
        //        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        //        bitmapImage.EndInit();
        //        return bitmapImage;
        //    }
        //}

        private BitmapImage BitmapToImageSource(System.Drawing.Bitmap bitmap)
        {
            using (var memory = new MemoryStream())
            {
                using (var tempBitmap = new System.Drawing.Bitmap(bitmap)) // Clone it to prevent conflicts
                {
                    tempBitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                }
                memory.Position = 0;
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                return bitmapImage;
            }
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopVideo();
        }

        private async Task SendFrames(TcpClient client)
        {
            while (client != null && client.Connected)
            {
                // Add a delay to simulate frame rate
                await Task.Delay(33); // ~30 FPS
                byte[] frameData = CaptureFrame();
                if (frameData != null)
                {
                    NetworkStream stream = client.GetStream();
                    byte[] frameSize = BitConverter.GetBytes(frameData.Length);
                    byte[] dataToSend = frameSize.Concat(frameData).ToArray();
                    await stream.WriteAsync(dataToSend, 0, dataToSend.Length);
                    Console.WriteLine("Frame sent.");
                }
            }
        }

        //private byte[] CaptureFrame()
        //{
        //    lock (frameLock)
        //    {
        //        if (latestFrame != null)
        //        {
        //            return BitmapToByteArray(latestFrame);
        //        }
        //        return null;
        //    }
        //}

        private byte[] CaptureFrame()
        {
            lock (frameLock)
            {
                if (latestFrame != null)
                {
                    try
                    {
                        return BitmapToByteArray(latestFrame);
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => MessageBox.Show($"Error capturing frame: {ex.Message}"));
                    }
                }
                return null;
            }
        }

    }
}
