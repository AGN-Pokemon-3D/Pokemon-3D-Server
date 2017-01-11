﻿using Amib.Threading;
using Modules.System;
using Modules.System.Net;
using Modules.System.Threading;
using Pokemon_3D_Server_Core.Interface;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using static Pokemon_3D_Server_Core.Collections.LogTypeCollection;
using static Pokemon_3D_Server_Core.Server.Game.Server.Package.Package;

namespace Pokemon_3D_Server_Core.Server.Game.Server
{
    public class Listener : IModules
    {
        /// <summary>
        /// Get the name of the module.
        /// </summary>
        public string Name { get { return "Game Server Listener"; } }

        /// <summary>
        /// Get the version of the module.
        /// </summary>
        public string Version { get { return "0.54"; } }

        private TcpListener TcpListener;
        private ThreadHelper Thread = new ThreadHelper();
        private IWorkItemsGroup ThreadPool = new SmartThreadPool().CreateWorkItemsGroup(Environment.ProcessorCount);
        private bool IsActive = false;

        /// <summary>
        /// Start the module.
        /// </summary>
        public void Start()
        {
            try
            {
                if (IPAddressHelper.GetPublicIP() == null)
                {
                    Core.Logger.Log("Network is not available.", LogTypes.Warning);
                    Stop();
                }
                else
                {
                    TcpListener = new TcpListener(new IPEndPoint(IPAddress.Any, Core.Settings.Server.Port));
                    TcpListener.AllowNatTraversal(true);
                    TcpListener.Start();

                    Thread.Add(() =>
                    {
                        Core.Logger.Log("Pokemon 3D Listener initialized.");
                        IsActive = true;

                        do
                        {
                            try
                            {
                                TcpClient client = TcpListener.AcceptTcpClient();

                                ThreadPool.QueueWorkItem(() =>
                                {
                                    if (client != null)
                                        Core.TcpClientCollection.Add(client);
                                });
                            }
                            catch (ThreadAbortException) { return; }
                            catch (Exception) { }
                        } while (IsActive);
                    });

                    if (Core.Settings.Server.OfflineMode || Core.Settings.Server.GameMode.NeedOfflineMode())
                        Core.Logger.Log("Players with offline profile can join the server.");

                    if (CheckPortOpen())
                    {
                        Core.Logger.Log($"Server started. Players can join using the following address: {Core.Settings.Server.IPAddress}:{Core.Settings.Server.Port.ToString()} (Global), {IPAddressHelper.GetPrivateIP()}:{Core.Settings.Server.Port.ToString()} (Local) and with the following GameMode: {Core.Settings.Server.GameMode}.");

                        if (Core.Settings.Server.CheckPort)
                        {
                            Thread.Add(() =>
                            {
                                Stopwatch sw = new Stopwatch();
                                sw.Start();

                                Core.Logger.Log("Port check is now enabled.");

                                do
                                {
                                    if (sw.Elapsed.TotalMinutes >= 15)
                                    {
                                        if (CheckPortOpen())
                                        {
                                            Core.Logger.Log("Port check cycle completed. Result: True.");
                                            sw.Restart();
                                        }
                                        else
                                        {
                                            Core.Logger.Log("Port check cycle completed. Result: False.");
                                            sw.Restart();
                                        }
                                    }
                                    else
                                        Thread.Sleep(1000);
                                } while (IsActive);
                            });
                        }
                    }
                    else
                    {
                        Core.Logger.Log($"The specific port {Core.Settings.Server.Port.ToString()} is not opened. External/Global IP will not accept new players.");
                        Core.Logger.Log($"Server started. Players can join using the following address: {IPAddressHelper.GetPrivateIP()}:{Core.Settings.Server.Port.ToString()} (Local) and with the following GameMode: {Core.Settings.Server.GameMode}.");
                    }
                }
            }
            catch (Exception ex)
            {
                ex.CatchError();
                Stop();
            }
        }

        /// <summary>
        /// Stop the module.
        /// </summary>
        public void Stop()
        {
            IsActive = false;

            if (TcpListener != null) TcpListener.Stop();
            if (Thread.Count > 0) Thread.Dispose();
            if (Core.TcpClientCollection.Count > 0) Core.TcpClientCollection.Dispose();
        }

        /// <summary>
        /// Check if the port is open.
        /// </summary>
        private bool CheckPortOpen()
        {
            using (TcpClient tcpClient = new TcpClient())
            {
                try
                {
                    tcpClient.SendTimeout = 10000;
                    tcpClient.ReceiveTimeout = 10000;

                    // Pokemon 3D Port.
                    tcpClient.ConnectAsync(Core.Settings.Server.IPAddress, Core.Settings.Server.Port).Wait(10000);

                    if (tcpClient.GetStream() != null)
                    {
                        using (StreamWriter writer = new StreamWriter(tcpClient.GetStream()) { AutoFlush = true })
                        {
                            using (StreamReader reader = new StreamReader(tcpClient.GetStream()))
                            {
                                writer.WriteLine(new Package.Package(PackageTypes.ServerDataRequest, "", null));

                                if (string.IsNullOrWhiteSpace(reader.ReadLine()))
                                    return false;
                                else
                                    return true;
                            }
                        }
                    }
                    else
                        return false;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }
}
