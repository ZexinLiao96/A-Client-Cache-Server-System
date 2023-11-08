# README

This is a tiny project I completed when studying parallel and distributed computing. It represents a client-cache-server system which communicate with each other using `TcpClient`. The functionality it achieves includes:

1. Client:
   - Display a list of available files on the server.
   - Enable file selection for download.
   - Show the contents of the downloaded file, specifically images.
2. Cache:
  - Use cached data to construct the requested file when available.
  - Download only the missing data from the server.
  - Maintain a log recording cache activities, including the percentage of the file constructed from cached data.
  - GUI interface to view the cache log, list and contents of cached data fragments in hexadecimal form.
  - Cached data fragments should be around 2KB in size.
  - Include a function to clear the cache.
3. Server:
  - Provide an operation to download a file by name.
  - List available file names on the server.
  - Support operations for the cache to download file fragments.

A sample screen-shot of the applications is as follow:

![3](C:\Users\liaoz\Downloads\3.png)

The project is written in C#. I had only Java programming experience before undertaking this project and it only took me a week to complete it.