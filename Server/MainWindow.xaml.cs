using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Server;

public partial class MainWindow
{
    private Socket localSocket;
    private Socket cacheSocket;
    private CancellationTokenSource CancellationTokenSource;

    private const int AVERAGE_FRAGMENT_SIZE = 2048;
    private const int WINDOW_SIZE = 48;

    private string fileList = "fileList|";

    public MainWindow()
    {
        InitializeComponent();
        DisplayFiles();
    }

    //display the file list on the GUI interface
    private void DisplayFiles()
    {
        //get all the filenames and save them in a string.
        string directoryPath = @".\files\items";
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var directoryInfo = new DirectoryInfo(directoryPath);
        var files = directoryInfo.GetFiles();
        fileList = "fileList|";
        Filelist.Items.Clear();

        //update the GUI interface.
        foreach (var file in files)
        {
            Filelist.Items.Add(file.Name.Substring(0, file.Name.LastIndexOf(".")));
            fileList = fileList + file.Name + "|";
        }
    }

    private void StartServer(object sender, RoutedEventArgs e)
    {
        //get socket ready and start watching for connection.
        CancellationTokenSource = new CancellationTokenSource();
        localSocket = CreateSocket();
        WatchConnection();

        //update the GUI interface.
        DialogContainer.AppendText("The server has been started! Listening port:8080 ...\r\n");
        StartServerButton.IsEnabled = false;
        StopServerButton.IsEnabled = true;
    }

    private Socket CreateSocket()
    {
        //create a socket and bind it to an IP end point.
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var ipHostEntry = Dns.GetHostEntry(Dns.GetHostName());
        var ipAddress = ipHostEntry.AddressList[0];
        var localEndPoint = new IPEndPoint(ipAddress, 8080);
        socket.Bind(localEndPoint);
        socket.Listen(10);

        return socket;
    }

    private async void WatchConnection()
    {
        try
        {
            //This line of code is used to resolve crashes caused by shutting down the server without waiting for a connection after starting it.
            cacheSocket = await Task.Run(() => localSocket.AcceptAsync(CancellationTokenSource.Token).AsTask(), CancellationTokenSource.Token);

            if (cacheSocket.RemoteEndPoint != null)
            {
                //update the GUI interface.
                string ip = ((IPEndPoint)cacheSocket.RemoteEndPoint).Address.ToString();
                string port = ((IPEndPoint)cacheSocket.RemoteEndPoint).Port.ToString();
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    DialogContainer.AppendText("Client" + ip + ":" + port + "connected.\r\n");
                }));

                //using thread to handle messages from cache, can use Task as well.
                Thread communicateThread = new Thread(ReceiveMessages)
                {
                    IsBackground = true
                };
                communicateThread.Start();
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("AcceptAsync operation has been canceled.");
        }
    }

    private void ReceiveMessages()
    {
        //using this method to handle messages received from cache.
        int receiveBufferSize = 2048;
        byte[] buffer = new byte[receiveBufferSize];

        while (true)
        {
            try
            {
                string messageReceived = Encoding.Default.GetString(buffer.Take(cacheSocket.Receive(buffer)).ToArray());

                if (messageReceived == "getFileList")
                {
                    //in this case cache is asking for file list, so send the file list straight away.
                    byte[] messageToSend = Encoding.Default.GetBytes(fileList);
                    cacheSocket.Send(messageToSend);
                }
                else if (messageReceived.StartsWith("getHashList|"))
                {
                    //in this case cache is asking for the fragment list file of a perticular image file.
                    //retrieve the fragment list file.
                    string fileName = messageReceived.Substring(12);
                    string filePath = new DirectoryInfo(@".\files\items\" + fileName + @".txt").FullName;
                    byte[] fileBytes = File.ReadAllBytes(filePath);

                    //send the fragment list file to cache, send the file name, file length and the file one by one.
                    string prompt = "hashList|" + fileName;//fileName = test1.bmp
                    byte[] messageToSend = Encoding.Default.GetBytes(prompt);
                    cacheSocket.Send(messageToSend);
                    byte[] fileLengthBytes = BitConverter.GetBytes(fileBytes.Length);
                    cacheSocket.Send(fileLengthBytes, 0, fileLengthBytes.Length, SocketFlags.None);
                    int bytesSent = 0;
                    while (bytesSent < fileBytes.Length)
                    {
                        bytesSent += cacheSocket.Send(fileBytes, bytesSent, fileBytes.Length - bytesSent, SocketFlags.None);
                    }
                }
                else if (messageReceived.StartsWith("getFrag|"))
                {
                    //in this case cache is asking for a perticular fragment file.
                    //retrieve the fragment file.
                    string fragName = messageReceived.Substring(8);
                    string fragPath = new DirectoryInfo(@".\files\fragments\" + fragName + @".dat").FullName;
                    byte[] fragBytes = File.ReadAllBytes(fragPath);

                    //send the fragment file.
                    byte[] fragLengthBytes = BitConverter.GetBytes(fragBytes.Length);
                    cacheSocket.Send(fragLengthBytes, 0, fragLengthBytes.Length, SocketFlags.None);
                    int bytesSent = 0;
                    while (bytesSent < fragBytes.Length)
                    {
                        bytesSent += cacheSocket.Send(fragBytes, bytesSent, fragBytes.Length - bytesSent, SocketFlags.None);
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                break;
            }
        }
    }

    private void StopServer(object sender, RoutedEventArgs e)
    {
        //shut down the server.
        CancellationTokenSource.Cancel();

        if (cacheSocket != null && cacheSocket.Connected)
        {
            cacheSocket.Shutdown(SocketShutdown.Both);
        }
        cacheSocket?.Close();
        localSocket?.Close();

        //update the GUI interface.
        DialogContainer.AppendText("The server has been stopped!\r\n");
        StartServerButton.IsEnabled = true;
        StopServerButton.IsEnabled = false;
    }

    private void UploadFiles(object sender, RoutedEventArgs e)
    {
        //allow user to select mutiple files to upload.
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp|All files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            string basePath = @".\files";
            Directory.CreateDirectory(basePath);

            //create subdirectories for fragments and items(the fragment list file of an image file).
            //the relationships between fragments and their own hash code are all recorded in hashes.txt (aka the hash file).
            //the relationships between an image file and the list of fragments needed to construct this file
            //    are recorded in a txt file with the same name as the image file under "items" folder (aka the relationship file).
            //all the fragments are saved under the "fragments" folder.
            string hashesFilePath = Path.Combine(basePath, "hashes.txt");
            string fragmentsPath = Path.Combine(basePath, @"fragments");
            string itemsPath = Path.Combine(basePath, @"items");
            Directory.CreateDirectory(fragmentsPath);
            Directory.CreateDirectory(itemsPath);

            //read existing hashes and store them in a dictionary, this is to achieve that if there is a same fragement file already exist,
            //    the server will not create another duplicate copy.
            Dictionary<string, int> existingHashes = new Dictionary<string, int>();
            if (File.Exists(hashesFilePath))
            {
                string[] hashesLines = File.ReadAllLines(hashesFilePath);
                foreach (string line in hashesLines)
                {
                    string[] parts = line.Split(':');
                    int index = int.Parse(parts[0]);
                    string hash = parts[1];
                    existingHashes[hash] = index;
                }
            }

            StringBuilder hashesContent = new StringBuilder(File.Exists(hashesFilePath) ? File.ReadAllText(hashesFilePath) : "");

            //for each image file uploaded, create fragments and coresponding hash code.
            foreach (string fileName in openFileDialog.FileNames)
            {
                //get a image file.
                FileInfo fileInfo = new FileInfo(fileName);
                byte[] fileData = File.ReadAllBytes(fileInfo.FullName);

                //get fragments for this image file.
                List<byte[]> fileFragments = SplitFileIntoFragments(fileData);

                StringBuilder imageFragmentsList = new StringBuilder();

                //for each fragment file, save it if its hash code does not exist.
                for (int i = 0; i < fileFragments.Count; i++)
                {
                    //calculate hash code for this fragment.
                    byte[] fragment = fileFragments[i];
                    string md5Hash = CalculateMD5Hash(fragment);

                    int fragmentIndex;
                    if (!existingHashes.ContainsKey(md5Hash))
                    {
                        //if this fragment file does not exist, generate a file name for it and save it to folder "fragments"
                        fragmentIndex = existingHashes.Count + 1;
                        string fragmentFilePath = Path.Combine(fragmentsPath, $"{fragmentIndex}.dat");
                        File.WriteAllBytes(fragmentFilePath, fragment);

                        //update the list of existing fragments for later comparison.
                        existingHashes[md5Hash] = fragmentIndex;
                        hashesContent.AppendLine($"{fragmentIndex}:{md5Hash}");
                    }
                    else
                    {
                        //if this fragment file exist, record the file name of that existing fragment file in the relationship file.
                        //this is essential for constructing the image file correctlly.
                        fragmentIndex = existingHashes[md5Hash];
                    }

                    //update the relationship file.
                    imageFragmentsList.Append(fragmentIndex + " ");
                }

                //save the image fragments list to a txt file with the same name as the image.
                string imageFragmentsFilePath = Path.Combine(itemsPath, fileInfo.Name + ".txt");
                File.WriteAllText(imageFragmentsFilePath, imageFragmentsList.ToString().TrimEnd());

                //update the GUI interface.
                DialogContainer.AppendText(fileInfo.Name + " has been added to the server!\r\n");
            }

            //update the hash file.
            File.WriteAllText(hashesFilePath, hashesContent.ToString());

            //update the GUI interface.
            DisplayFiles();

            //inform cache that the file list has changed if cache is connected.
            if (cacheSocket != null && cacheSocket.Connected)
            {
                byte[] messageToSend = Encoding.Default.GetBytes(fileList);
                cacheSocket.Send(messageToSend);
            }
        }
    }

    private List<byte[]> SplitFileIntoFragments(byte[] fileData)
    {
        //this method is using a Rabin fingerprint function to break a image file down to several fragments.
        List<byte[]> fileFragments = new List<byte[]>();
        int fragmentStart = 0;
        int fragmentEnd = 0;
        ulong fingerprint = 0;
        ulong prime = 2113;

        ulong baseValue = ModExp(2, WINDOW_SIZE, prime);

        for (int i = 0; i < fileData.Length; i++)
        {
            fingerprint *= 2;
            fingerprint += fileData[i];
            fingerprint %= prime;

            if (i >= WINDOW_SIZE)
            {
                fingerprint += prime - ((fileData[i - WINDOW_SIZE] * baseValue) % prime);
                fingerprint %= prime;
            }

            fragmentEnd++;

            if (fingerprint % AVERAGE_FRAGMENT_SIZE == AVERAGE_FRAGMENT_SIZE - 1 || fragmentEnd - fragmentStart >= 3072)
            {
                byte[] fragment = new byte[fragmentEnd - fragmentStart];
                Array.Copy(fileData, fragmentStart, fragment, 0, fragment.Length);
                fileFragments.Add(fragment);

                fragmentStart = fragmentEnd;
            }
        }

        if (fragmentEnd - fragmentStart > 0)
        {
            byte[] fragment = new byte[fragmentEnd - fragmentStart];
            Array.Copy(fileData, fragmentStart, fragment, 0, fragment.Length);
            fileFragments.Add(fragment);
        }

        return fileFragments;
    }

    private string CalculateMD5Hash(byte[] data)
    {
        //this method is to generate a MD5 hash code for a fragment.
        using (MD5 md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(data);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }

    private ulong ModExp(ulong baseValue, ulong exponent, ulong modulus)
    {
        //this method is a support method of the algorithm of creating fragments.
        ulong result = 1;
        baseValue %= modulus;

        while (exponent > 0)
        {
            if ((exponent & 1) == 1)
                result = (result * baseValue) % modulus;

            exponent >>= 1;
            baseValue = (baseValue * baseValue) % modulus;
        }

        return result;
    }


}