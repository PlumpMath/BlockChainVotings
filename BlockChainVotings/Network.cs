﻿using NetworkCommsDotNet.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NetworkCommsDotNet.Connections;
using System.Timers;
using NetworkCommsDotNet;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using NetworkCommsDotNet.Connections.TCP;

namespace BlockChainVotings
{
    public class Network
    {
        public List<Peer> Peers { get; }
        public List<Tracker> Trackers { get; }

        int normalPeersCount = 10;

        Timer t;

        public event EventHandler<MessageEventArgs> OnRequestBlocksMessage;
        public event EventHandler<MessageEventArgs> OnRequestTransactionsMessage;
        public event EventHandler<MessageEventArgs> OnBlocksMessage;
        public event EventHandler<MessageEventArgs> OnTransactionsMessage;



        public Network()
        {

            Trackers = new List<Tracker>();
            Peers = new List<Peer>();

            SetupLogging();


            t = new Timer(10000);
            t.Elapsed += (s, e) => RequestPeers();
            t.Elapsed += (s, e) => ConnectToPeers();
            t.Elapsed += (s, e) => ConnectToTrackers();

            NetworkComms.AppendGlobalConnectionEstablishHandler(OnConnectPeerDirect);

        }




        private void OnConnectPeerDirect(Connection connection)
        {
            if (connection.ConnectionInfo.ConnectionType == ConnectionType.TCP
                && !(connection.ConnectionInfo.LocalEndPoint as IPEndPoint).Address.Equals((connection.ConnectionInfo.RemoteEndPoint as IPEndPoint).Address)
                && (connection.ConnectionInfo.RemoteEndPoint as IPEndPoint).Port == CommonInfo.PeerPort)
            {
                AddPeer(connection.ConnectionInfo.RemoteEndPoint);
            }
        }


        void ParseTrackers(string[] addresses)
        {
            foreach (var line in addresses)
            {
                var parts = line.Split(':');

                IPAddress addr = new IPAddress(0);
                /*int port = 0;*/
                if (parts.Length >= 1 && IPAddress.TryParse(parts[0], out addr) /*&& int.TryParse(parts[1], out port)*/)
                {
                    EndPoint endPoint = new IPEndPoint(addr, CommonInfo.TrackerPort);

                    Tracker tracker = new Tracker(endPoint, Trackers);

                    //перенаправляем события трекера вне
                    tracker.OnRequestBlocksMessage += (s, e) =>
                    {
                        if (OnRequestBlocksMessage != null)
                            OnRequestBlocksMessage(s, e);
                    };

                    tracker.OnRequestTransactionsMessage += (s, e) => 
                    {
                        if (OnRequestTransactionsMessage != null)
                            OnRequestTransactionsMessage(s, e);
                    };

                    tracker.OnBlocksMessage += (s, e) => 
                    {
                        if (OnBlocksMessage != null)
                            OnBlocksMessage(s, e);
                    };

                    tracker.OnTransactionsMessage += (s, e) => 
                    {
                        if (OnTransactionsMessage != null)
                            OnTransactionsMessage(s, e);
                    };


                    //получение пиров
                    tracker.OnPeersMessageFromPeer += OnPeersMessage;
                    tracker.OnPeersMessageFromTracker += OnPeersMessage;

                    //коннект через трекер
                    tracker.OnConnectToPeerWithTrackerMessage += OnConnectToPeerWithTrackerMessage;

                    Trackers.Add(tracker);


                }
            }

        }

        private void OnConnectToPeerWithTrackerMessage(object sender, MessageEventArgs e)
        {
            var message = e.Message as ConnectToPeerWithTrackerMessage;

            var peerToConnect = Peers.FirstOrDefault(peer => peer.Address.Equals(message.SenderAddress));
            //если пира нет в списке, то добавляем его и подключаемся прямо через трекер
            if (peerToConnect == null)
            {
                AddPeer(message.SenderAddress, (sender as Tracker), true);
            }
            //если пир есть в списке, то просто подключаемся через трекер
            else if (peerToConnect.Status != PeerStatus.Connected)
            {
                peerToConnect.Connect(true);
            }
        }

        private void OnPeersMessage(object sender, MessageEventArgs e)
        {
            var message = e.Message as PeersMessage;

            //добавляем пир и если сообщение пришло от трекера, то указываем трекер
            foreach (var address in message.PeersAdresses)
            {
                AddPeer(address, (sender is Tracker) ? (sender as Tracker) : null);
            }
        }

        public void SendMessageToPeer(Message message, EndPoint address)
        {
            var peerReciever = Peers.FirstOrDefault(peer => peer.Address.Equals(address));
            if (peerReciever != null)
            {
                peerReciever.SendMessage(message);
            }
        }

        public void SendMessageToAllPeers(Message message)
        {
            foreach (var peer in Peers)
            {
                peer.SendMessage(message);
            }
        }

        private void PeerDiscovered(ShortGuid peerIdentifier, Dictionary<ConnectionType, List<EndPoint>> discoveredListenerEndPoints)
        {
            if (discoveredListenerEndPoints[ConnectionType.UDP].Any())
            {
                var address = discoveredListenerEndPoints[ConnectionType.UDP].First();
                (address as IPEndPoint).Port = CommonInfo.PeerPort;

                AddPeer(address);
            }
        }

        private void AddPeer(EndPoint address, Tracker tracker = null, bool withTracker = false)
        {
            if (!Peers.Any(peer => (peer.Address as IPEndPoint).Address.Equals((address as IPEndPoint).Address)))
            {
                var peer = new Peer(address, Peers, tracker);
                Peers.Add(peer);

                //перенаправляем события пира
                peer.OnRequestBlocksMessage += (s, e) => 
                {
                    if (OnRequestBlocksMessage != null)
                        OnRequestBlocksMessage(s, e);
                };

                peer.OnRequestTransactionsMessage += (s, e) => 
                {
                    if (OnRequestTransactionsMessage != null)
                        OnRequestTransactionsMessage(s, e);
                };

                peer.OnBlocksMessage += (s, e) => 
                {
                    if (OnBlocksMessage != null)
                        OnBlocksMessage(s, e);
                };

                peer.OnTransactionsMessage += (s, e) => 
                {
                    if (OnTransactionsMessage != null)
                        OnTransactionsMessage(s, e);
                };

                peer.OnPeersMessage += (s, e) => OnPeersMessage(s, e);



                peer.Connect(withTracker);
            }
        }

        public void RequestPeers()
        {
            //считаем сколько нужно пиров
            int needPeersCount = normalPeersCount - Peers.Where(peer => peer.Status == PeerStatus.Connected).Count();
            if (needPeersCount <= 0) return;


            //сначала ищем пиры без трекера
            try {
                //PeerDiscovery.DiscoverPeersAsync(PeerDiscovery.DiscoveryMethod.UDPBroadcast);
            }
            catch (InvalidOperationException e) { }



            //пиры подключенные прямо (сортируем по кол-ву запросов)
            var connectedPeersDirect = Peers.Where(peer => peer.Status == PeerStatus.Connected && peer.ConnectionType == ConnectionMode.Direct)
                .OrderBy(peer => peer.PeersRequestsCount);

            //подключенные трекеры (сортируем по кол-ву запросов)
            var connectedTrackers = Trackers.Where(tracker => tracker.Status == TrackerStatus.Connected)
                .OrderBy(tracker => tracker.PeersRequestsCount);

            //пиры подключенные через трекер (сортируем по кол-ву запросов)
            var connectedPeersWithTracker = Peers.Where(peer => peer.Status == PeerStatus.Connected && peer.ConnectionType == ConnectionMode.WithTracker)
                .OrderBy(peer => peer.PeersRequestsCount);

            //если есть подключенные пиры и трекеры
            if (connectedPeersDirect.Any() && connectedTrackers.Any())
            {
                var firstPeerDirect = connectedPeersDirect.First();
                var firstTracker = connectedTrackers.First();

                //запрашиваем у первого прямого пира, если к нему было меньше запросов чем к первому трекеру
                if (firstPeerDirect.PeersRequestsCount <= firstTracker.PeersRequestsCount)
                {
                    firstPeerDirect.RequestPeers(needPeersCount);
                }
                //если есть подключенные через трекеры пиры
                else if (connectedPeersWithTracker.Any())
                {
                    var firstPeerWithTracker = connectedPeersWithTracker.First();

                    //запрашиваем у первого трекера, если к нему было меньше запросов чем к первому подключенному через трекер пиру
                    if (firstTracker.PeersRequestsCount <= firstPeerWithTracker.PeersRequestsCount)
                        firstTracker.RequestPeersFromTracker(needPeersCount);
                    else
                        firstPeerWithTracker.RequestPeers(needPeersCount);

                }
                else
                {
                    firstTracker.RequestPeersFromTracker(needPeersCount);
                }

            }
            else if (connectedPeersDirect.Any())
            {
                var firstPeerDirect = connectedPeersDirect.First();
                firstPeerDirect.RequestPeers(needPeersCount);
            }
            else if (connectedTrackers.Any())
            {
                var firstTracker = connectedTrackers.First();
                firstTracker.RequestPeersFromTracker(needPeersCount);
            }
        }



        private void ConnectToTrackers()
        {
            foreach (var tracker in Trackers)
            {
                if (tracker.Status == TrackerStatus.Disconnected) tracker.Connect();
            }
        }

        private void ConnectToPeers()
        {
            foreach (var peer in Peers)
            {
                if (peer.Status != PeerStatus.Connected) peer.Connect();
            }
        }

        public void Disconnect()
        {
            while (Peers.Count>0)
            {
                Peers.First().DisconnectDirect();
            }
            while (Trackers.Count > 0)
            {
                Trackers.First().Disconnect();
            }

            t.Stop();


            PeerDiscovery.DisableDiscoverable();
            Connection.StopListening();
            NetworkComms.CloseAllConnections();
            NetworkComms.Shutdown();

            NetworkComms.Logger.Warn("Client stopped");
        }

        public void Connect(string[] trackers)
        {
            ParseTrackers(trackers);

            if (CommonInfo.GetLocalEndPoint() != null)
            {
                PeerDiscovery.MinTargetLocalIPPort = CommonInfo.DiscoveryPort;
                PeerDiscovery.MaxTargetLocalIPPort = CommonInfo.DiscoveryPort;
                PeerDiscovery.OnPeerDiscovered += PeerDiscovered;
                PeerDiscovery.EnableDiscoverable(PeerDiscovery.DiscoveryMethod.UDPBroadcast, CommonInfo.GetLocalEndPoint(true));
            }


            TCPConnection.StartListening(CommonInfo.GetLocalEndPoint() as IPEndPoint, false);

            NetworkComms.Logger.Warn("Client started");

            //только для дебага !!!!!!
            ConnectToTrackers();
            RequestPeers();
            ConnectToPeers();
            //t.Start();
        }

        private void SetupLogging()
        {
            LiteLogger logger = new LiteLogger(LiteLogger.LogMode.ConsoleAndLogFile, "log.txt");
            NetworkComms.EnableLogging(logger);

            NetworkComms.Logger.Warn("==================== Initialisation ====================");


        }


    }
}
