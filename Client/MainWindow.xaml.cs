using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Client;

public partial class MainWindow
{
    private Socket cacheSocket;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ConnectServer(object sender, RoutedEventArgs e)
    {
        try
        {
            cacheSocket = CreateSocket();
        }
        catch (Exception)
        {
            MessageBox.Show("Server no response!");
            return;
        }

        ConnectServerButton.IsEnabled = false;
        DisconnectButton.IsEnabled = true;
        ShowFilesButton.IsEnabled = true;
    }

    private static Socket CreateSocket()
    {
        var ipHostEntry = Dns.GetHostEntry(Dns.GetHostName());
        var ipAddress = ipHostEntry.AddressList[0];
        var ipEndPoint = new IPEndPoint(ipAddress, 8081);
        Socket newSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        newSocket.Connect(ipEndPoint);
        return newSocket;
    }

    private void DisconnectFromServer(object sender, RoutedEventArgs e)
    {
        try
        {
            cacheSocket.Shutdown(SocketShutdown.Both);
            cacheSocket.Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message);
            return;
        }

        ConnectServerButton.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
        ShowFilesButton.IsEnabled = false;
        DownloadFileButton.IsEnabled = false;
    }


    private void GetFileList(object sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            byte[] messageToSend = Encoding.Default.GetBytes("getFileList");
            cacheSocket.Send(messageToSend);

            byte[] receiveBuffer = new byte[2048]; //这里默认file List的长度不会超过2048字节，后续需要调整。
            int receivedMessageLength = cacheSocket.Receive(receiveBuffer);

            string fileListString = Encoding.Default.GetString(receiveBuffer, 0, receivedMessageLength);
            Dispatcher.Invoke(() =>
            {
                fileListString = fileListString.Substring(9);
                string[] files = fileListString.Split("|");
                Array.Resize(ref files, files.Length - 1);//删除最后一个多余值
                FileList.Items.Clear();
                foreach (string file in files)
                {
                    FileList.Items.Add(file.Substring(0,file.LastIndexOf(".")));
                }

                return Task.CompletedTask;
            });
        });
    }

    private void ItemSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        DownloadFileButton.IsEnabled = true;
    }

    private void DownloadSelectedFile(object sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            byte[]? messageToSend = null;
            string? fileName = "";

            Dispatcher.Invoke(() =>
            {
                fileName = FileList.SelectedItem.ToString();
                messageToSend = Encoding.Default.GetBytes("fileItem|" + fileName);
            });

            if (messageToSend != null)
            {
                cacheSocket.Send(messageToSend);
            }

            // 接收图片长度
            byte[] imageLengthBytes = new byte[4];
            cacheSocket.Receive(imageLengthBytes, 0, imageLengthBytes.Length, SocketFlags.None);
            int imageLength = BitConverter.ToInt32(imageLengthBytes);

            // 接收图片
            byte[] buffer = new byte[4096];
            MemoryStream receivedImage = new MemoryStream();
            int bytesRead;
            int totalBytesRead = 0;

            while (totalBytesRead < imageLength)
            {
                bytesRead = cacheSocket.Receive(buffer, 0, buffer.Length, SocketFlags.None);
                receivedImage.Write(buffer, 0, bytesRead);
                totalBytesRead += bytesRead;
            }

            // 将图片保存到D盘根目录
            string outputPath = @".\files\" + fileName;
            string absoluteOutputPath = Path.GetFullPath(outputPath);
            File.WriteAllBytes(outputPath, receivedImage.ToArray());

            Dispatcher.Invoke(() =>
            {
                //展示图片到窗口
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(absoluteOutputPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // 忽略图片缓存
                bitmap.EndInit();
                bitmap.Freeze(); // 确保图片资源在非UI线程上可以被访问

                PreviewWindow.Source = bitmap;
            });
        });
    }
}