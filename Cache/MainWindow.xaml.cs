using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Cache
{
    public partial class MainWindow
    {
        private Socket serverSocket;
        private Socket localSocket;
        private Socket clientSocket;
        private CancellationTokenSource cancellationTokenSource;
        private string fileList = "";

        public MainWindow()
        {
            InitializeComponent();
            DisplayFragments();
        }

        private void StartCacheServer(object sender, RoutedEventArgs e)
        {
            //initialise the two sockets.
            try
            {
                serverSocket = CreateServerSocket();
                localSocket = CreateLocalSocket();
            }
            catch (Exception ex)
            {
                MessageBox.Show("1" + ex.Message);
                return;
            }

            //use cancellationToken to terminate the application safely.
            cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            //using two threads to handle messages from client and server.
            prepareFileList();
            Task.Run(() => HandleClientMessage(cancellationToken));
            Task.Run(() => HandleServerMessage(cancellationToken));

            //update GUI interface.
            StartCacheServerButton.IsEnabled = false;
            StopCacheServerButton.IsEnabled = true;
        }

        private Socket CreateServerSocket()
        {
            //simply connect to an IP end point
            var ipHostEntry = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = ipHostEntry.AddressList[0];
            var ipEndPoint = new IPEndPoint(ipAddress, 8080);
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(ipEndPoint);

            return socket;
        }

        private Socket CreateLocalSocket()
        {
            //bind to an IP end point and start listening for connection.
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var ipHostEntry = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = ipHostEntry.AddressList[0];
            var localEndPoint = new IPEndPoint(ipAddress, 8081);
            socket.Bind(localEndPoint);
            socket.Listen(10);
            LogContainer.AppendText("The cache has been started! Listening port:8081 ...\r\n");

            return socket;
        }

        private void prepareFileList()
        {
            //everytime the cache start, it will proactively update the file list it has by asking from the server.
            Task.Run(() =>
            {
                //send request and receive new file list from server.
                byte[] messageToSend = Encoding.Default.GetBytes("getFileList");
                serverSocket.Send(messageToSend);
                byte[] receiveBuffer = new byte[2048]; //这里默认file List的长度不会超过2048字节，后续需要调整。
                int receivedMessageLength = serverSocket.Receive(receiveBuffer);

                //check whether it is necessary to update the local file list.
                string newFileList = Encoding.Default.GetString(receiveBuffer, 0, receivedMessageLength);
                if (!fileList.Equals(newFileList))
                {
                    fileList = newFileList;
                }

                //record this activity in log.
                string newLog = "File list updated at " + DateTime.Now.ToString("HH:mm:ss yyyy-MM-dd") + "\r\n";
                UpdateLog(newLog);

                return Task.CompletedTask;
            });
        }

        private async void HandleClientMessage(CancellationToken cancellationToken)
        {
            int bufferSize = 2048;//2048 bytes is long enough to store any requests cache can possibly get from client.
            byte[] buffer = new byte[bufferSize];

            //waiting for client to connect.
            clientSocket = await localSocket.AcceptAsync();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    //receive a message from client.
                    int receivedBytes = clientSocket.Receive(buffer);
                    string messageReceived = Encoding.Default.GetString(buffer, 0, receivedBytes);

                    if (messageReceived == "getFileList")
                    {
                        //client wants the file list, send it to client straight away.
                        //Other parts of the code have made sure that cache will always have the newest file list.
                        byte[] messageToSend = Encoding.Default.GetBytes(fileList);//这里同样需要分两步发送，先发送长度，再发送实际内容。
                        clientSocket.Send(messageToSend);
                    }
                    else if (messageReceived.StartsWith("fileItem|"))
                    {
                        //record this activity in log.
                        string fileName = messageReceived.Substring(9);
                        string newLog = "user request: file " + fileName + " at " + DateTime.Now.ToString("HH:mm:ss yyyy-MM-dd") + "\r\n";
                        Dispatcher.Invoke(() =>
                        {
                            UpdateLog(newLog);
                        });

                        //client wants the a picture, cache sends request for fragment information to server.
                        byte[] messageToSend = Encoding.Default.GetBytes("getHashList|" + fileName);
                        serverSocket.Send(messageToSend);
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show("2" + e.Message);
                    break;
                }
            }

            clientSocket.Close();
        }

        private void HandleServerMessage(CancellationToken cancellationToken)
        {
            int receiveBufferSize = 2048;
            byte[] buffer = new byte[receiveBufferSize];

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    //receive a message from server.
                    int receivedBytes = serverSocket.Receive(buffer);
                    string messageReceived = Encoding.Default.GetString(buffer, 0, receivedBytes);


                    if (messageReceived.StartsWith("fileList|"))
                    {
                        //messages like this represent that cache need to update file list, so cache saves the new file list.
                        fileList = messageReceived;

                        //record this activity in log.
                        string newLog = "File list updated at " + DateTime.Now.ToString("HH:mm:ss yyyy-MM-dd") + "\r\n";
                        Dispatcher.Invoke(() =>
                        {
                            UpdateLog(newLog);
                        });
                    }
                    else if (messageReceived.StartsWith("hashList|"))
                    {
                        //messages like this represent that server is going to send a file containing the fragment list which is needed to construct a picture.
                        string hashFileName = messageReceived.Substring(9);

                        //cache receive the file and keep it in a memory stream for later use.
                        byte[] hashFileLengthBytes = new byte[4];
                        serverSocket.Receive(hashFileLengthBytes, 0, hashFileLengthBytes.Length, SocketFlags.None);
                        int hashFileLength = BitConverter.ToInt32(hashFileLengthBytes);
                        byte[] hashBuffer = new byte[4096];
                        MemoryStream receivedHashFile = new MemoryStream();
                        int bytesRead;
                        int totalBytesRead = 0;
                        while (totalBytesRead < hashFileLength)
                        {
                            bytesRead = serverSocket.Receive(hashBuffer, 0, hashBuffer.Length, SocketFlags.None);
                            receivedHashFile.Write(hashBuffer, 0, bytesRead);
                            totalBytesRead += bytesRead;
                        }

                        //transferring the content received into a string array for later comparing.
                        string receivedContent = Encoding.UTF8.GetString(receivedHashFile.ToArray());
                        string[] fragNames = receivedContent.Split(" ");

                        //retrieve all the existing fragment files and record their names in a string array for later comparing.
                        string[] fragsPath = Directory.GetFiles(@".\hashes\", "*.dat");
                        string[] storedFragNames = new string[fragsPath.Length];
                        for (int i = 0; i < fragsPath.Length; i++)
                        {
                            storedFragNames[i] = Path.GetFileNameWithoutExtension(fragsPath[i]);
                        }

                        //comparing the two string array and find the missing fragments.
                        List<string> missingFragNamesList = new List<string>();
                        foreach (string fragName in fragNames)
                        {
                            if (!storedFragNames.Contains(fragName))
                            {
                                missingFragNamesList.Add(fragName);
                            }
                        }
                        string[] missingFragNames = missingFragNamesList.ToArray();

                        //record this activity in log.
                        double num = (1 - ((double)missingFragNames.Length / fragNames.Length)) * 100;
                        double percentage = Math.Truncate(num * 100) / 100;
                        string newLog = "response: " + percentage + "% of file " + hashFileName + " was constructed with the cached data\r\n";
                        Dispatcher.Invoke(() =>
                        {
                            UpdateLog(newLog);
                        });

                        //if needed, retrieve the missing fragments from server.
                        if (missingFragNames.Length > 0)
                        {

                            //retrieving missing fragments one by one and save it in local.
                            foreach (string fragName in missingFragNames)
                            {
                                //asking for a particular fragment.
                                byte[] messageToSend = Encoding.Default.GetBytes("getFrag|" + fragName);
                                serverSocket.Send(messageToSend);

                                //receiving the fragment.
                                byte[] fragFileLengthBytes = new byte[4];
                                serverSocket.Receive(fragFileLengthBytes, 0, fragFileLengthBytes.Length, SocketFlags.None);
                                int fragFileLength = BitConverter.ToInt32(fragFileLengthBytes);
                                byte[] fragBuffer = new byte[4096];
                                MemoryStream receivedFragFile = new MemoryStream();
                                int fragBytesRead;
                                int totalFragBytesRead = 0;
                                while (totalFragBytesRead < fragFileLength)
                                {
                                    fragBytesRead = serverSocket.Receive(fragBuffer, 0, fragBuffer.Length, SocketFlags.None);
                                    receivedFragFile.Write(fragBuffer, 0, fragBytesRead);
                                    totalFragBytesRead += fragBytesRead;
                                }

                                //save the fragment.
                                string savePath = Path.Combine(@".\hashes\", fragName + ".dat");
                                File.WriteAllBytes(savePath, receivedFragFile.ToArray());
                            }
                        }

                        //update the GUI interface.
                        Dispatcher.Invoke((() =>
                        {
                            DisplayFragments();
                        }));

                        //finally, assemble the picture client was asking for and send it to client.
                        MemoryStream combinedImageStream = new MemoryStream();
                        foreach (string fragName in fragNames)
                        {
                            string fragPath = Path.Combine(@".\hashes\", fragName + ".dat");
                            byte[] fragData = File.ReadAllBytes(fragPath);
                            combinedImageStream.Write(fragData, 0, fragData.Length);
                        }
                        byte[] combinedImageData = combinedImageStream.ToArray();
                        byte[] imageLengthBytes = BitConverter.GetBytes(combinedImageData.Length);

                        clientSocket.Send(imageLengthBytes);
                        clientSocket.Send(combinedImageData);
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show("3" + e.Message);
                    break;
                }
            }

            serverSocket.Close();
        }

        private void DisplayFragments()
        {
            //get the directory information for the fragment folder.
            DirectoryInfo directoryInfo = new DirectoryInfo(@".\hashes\");

            //check if the directory exists, if not, create it.
            if (!directoryInfo.Exists)
            {
                Directory.CreateDirectory(@".\hashes\");
            }

            //get all the .dat files (fragment files) in the folder.
            var datFiles = directoryInfo.GetFiles("*.dat");

            //clear the FragmentList ListBox.
            FragmentList.Items.Clear();

            //sort the files by their numeric value in ascending order.
            var sortedDatFiles = datFiles.OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f.Name)));

            //add the sorted file names (without the extension) to the FragmentList ListBox.
            foreach (var file in sortedDatFiles)
            {
                FragmentList.Items.Add(Path.GetFileNameWithoutExtension(file.Name));
            }
        }


        private void UpdateLog(string newLog)
        {
            string logDirectoryPath = @".\log\";
            string logFilePath = Path.Combine(logDirectoryPath, "log.txt");

            //check if the log directory exists, if not, create it.
            if (!Directory.Exists(logDirectoryPath))
            {
                Directory.CreateDirectory(logDirectoryPath);
            }

            //append text into the log file, and update GUI interface.
            File.AppendAllText(logFilePath, newLog);
            LogContainer.AppendText(newLog);
        }

        private void ShowLogFile(object sender, RoutedEventArgs e)
        {
            string logDirectoryPath = @".\log\";
            string logFilePath = Path.Combine(logDirectoryPath, "log.txt");

            //check if the log directory exists, if not, create it.
            if (!Directory.Exists(logDirectoryPath))
            {
                Directory.CreateDirectory(logDirectoryPath);
            }

            //check if the log file exists, if not, create it.
            if (!File.Exists(logFilePath))
            {
                File.Create(logFilePath).Close();
            }

            //open the log file.
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(logFilePath)
                {
                    UseShellExecute = true,
                    Verb = "open"
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}");
            }
        }

        private void ClearLog(object sender, RoutedEventArgs e)
        {
            //this method is for deleting the log file.
            string logPath = @".\log\log.txt";

            if (File.Exists(logPath))
            {
                MessageBoxResult result = MessageBox.Show("Are you sure you want to delete the log file?", "Confirm Delete",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        File.Delete(logPath);
                        LogContainer.Clear();
                        MessageBox.Show("Log file has been deleted.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("An error occurred while deleting the log file: " + ex.Message);
                    }
                }
            }
            else
            {
                MessageBox.Show("Log file not found.");
            }
        }

        private void StopCacheServer(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource.Cancel();

            StartCacheServerButton.IsEnabled = true;
            StopCacheServerButton.IsEnabled = false;
        }

        private void FragmentList_Selected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ShowFragmentDetailButton.IsEnabled = true;
        }

        private void ShowFragmentDetail(object sender, RoutedEventArgs e)
        {
            //get the selected item from the FragmentList ListBox.
            string selectedItem = FragmentList.SelectedItem.ToString();

            //get the file path for the selected item.
            string filePath = Path.Combine(@".\hashes\", selectedItem + ".dat");

            //check if the file exists.
            if (File.Exists(filePath))
            {
                //read the file content.
                byte[] fileContent = File.ReadAllBytes(filePath);

                //convert the file content to a hexadecimal string.
                StringBuilder hexStringBuilder = new StringBuilder(fileContent.Length * 2);
                foreach (byte b in fileContent)
                {
                    hexStringBuilder.AppendFormat("{0:x2}", b);
                }
                string hexString = hexStringBuilder.ToString();

                //show the hexadecimal content in a MessageBox.
                MessageBox.Show(hexString);
            }
            else
            {
                MessageBox.Show($"File '{selectedItem}.dat' not found.");
            }
        }

        private void ClearCache(object sender, RoutedEventArgs e)
        {
            string folderPath = @".\hashes\";
            DirectoryInfo cachedFragsFolder = new DirectoryInfo(folderPath);

            if (cachedFragsFolder.Exists)
            {
                try
                {
                    //delete all files in the folder.
                    foreach (FileInfo fragFile in cachedFragsFolder.GetFiles())
                    {
                        fragFile.Delete();
                    }

                    //update the GUI interface.
                    DisplayFragments();
                    ShowFragmentDetailButton.IsEnabled = false;
                    MessageBox.Show("Cache cleared successfully.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error clearing cache: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show($"Folder '{folderPath}' not found.");
            }
        }
    }
}