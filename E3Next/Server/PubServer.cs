﻿using E3Core.Processors;
using E3Core.Settings;
using E3Core.Utility;
using MonoCore;
using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace E3Core.Server
{

    /// <summary>
    /// Sends events out to the UI client, or whoever
    /// </summary>
    public class PubServer
    {
        private static IMQ MQ = E3.MQ;
        class topicMessagePair
        {
            public string topic;
            public string message;
        }

        Task _serverThread = null;

        public static ConcurrentQueue<string> IncomingChatMessages = new ConcurrentQueue<string>();
        public static ConcurrentQueue<string> MQChatMessages = new ConcurrentQueue<string>();
        public static ConcurrentQueue<string> CommandsToSend = new ConcurrentQueue<string>();
        private static ConcurrentQueue<topicMessagePair> _topicMessages = new ConcurrentQueue<topicMessagePair>();

        public static Int32 PubPort = 0;



        public void Start(Int32 port)
        {
            PubPort = port;
            string localIP = e3util.GetLocalIPAddress();
            string filePath = BaseSettings.GetSettingsFilePath($"{E3.CurrentName}_{E3.ServerName}_pubsubport.txt");

            //System.IO.File.Delete(filePath);
            bool updatedFile = false;
            Int32 counter = 0;
            while(!updatedFile)
            {
                counter++;
				try
				{

					System.IO.File.WriteAllText(filePath, port.ToString() + "," + localIP);
					updatedFile = true;
				}
				catch (Exception ex)
				{
                    System.Threading.Thread.Sleep(100);
				    if(counter>20) //allow up 2 seconds worth of failures before we throw an exception.
                    {
                        throw new Exception($"Cannot write out the pubsubport file {filePath}, some other process is using it. Try manually deleting it. ErrorMessage:" + ex.Message);
                    }
                }

			}
			_serverThread = Task.Factory.StartNew(() => { Process(filePath); }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

        }
        public  static void AddTopicMessage(string topic, string message)
        {
            topicMessagePair t = new topicMessagePair() { topic = topic, message = message };
            _topicMessages.Enqueue(t);
        }
        private void Process(string filePath)
        {
            AsyncIO.ForceDotNet.Force();
            using (var pubSocket = new PublisherSocket())
            {
                pubSocket.Options.SendHighWatermark = 50000;
                
                pubSocket.Bind("tcp://0.0.0.0:" + PubPort.ToString());
                
                while (Core.IsProcessing && E3.NetMQ_PubServerThradRun)
                {
                   
                    while (_topicMessages.Count > 0)
                    {
                        if (_topicMessages.TryDequeue(out var value))
                        {

                            pubSocket.SendMoreFrame(value.topic).SendFrame(value.message);
                        }
                    }
                   while(IncomingChatMessages.Count > 0)
                    {
                        string message;
                        if (IncomingChatMessages.TryDequeue(out message))
                        {

                            pubSocket.SendMoreFrame("OnIncomingChat").SendFrame(message);
                        }
                    }
                   while (MQChatMessages.Count > 0)
                    {
                        string message;
                        if (MQChatMessages.TryDequeue(out message))
                        {

                            pubSocket.SendMoreFrame("OnWriteChatColor").SendFrame(message);

                        }
                    }
                    while(CommandsToSend.Count > 0)
                    {
                        string message;
                        if (CommandsToSend.TryDequeue(out message))
                        {

                            pubSocket.SendMoreFrame("OnCommand").SendFrame(message);

                        }
                    }
                    System.Threading.Thread.Sleep(1);
                }
                try
                {
					System.IO.File.Delete(filePath);

				}
				catch (Exception ex)
                {
                    MQ.Write("Issue deleting pubsub.txt file");
                }
				MQ.Write("Shutting down PubServer Thread.");
            }
        }
    }
}
