using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Runtime.InteropServices;
using SIPSorcery;
using SIPSorcery.Net;
using static System.Net.Mime.MediaTypeNames;
using SIPSorceryMedia.Abstractions;

namespace HarmonyServer
{
    internal class Program
    {
        async static Task Main(string[] args)
        {
            var turnServer = new Server(5649);
            await turnServer.StartAsync();
        }
    }

    public class Server
    {
        private readonly int _port;
        private HttpListener _httpListener;
        private readonly ConcurrentDictionary<string, Room> _activeRooms = new();
        private static readonly Random _random = new();

        private List<Client> _clients = new();

        private string GenerateRoomCode()
        {
            string code;
            do
            {
                code = _random.Next(100000, 999999).ToString(); // Генерация 6-значного числа
            } while (_activeRooms.ContainsKey(code)); // Проверка уникальности
            return code;
        }
        public string CreateRoom(string ownerId)
        {
            var code = GenerateRoomCode();
            var room = new Room { Code = code, OwnerId = ownerId };
            _activeRooms.TryAdd(code, room);
            return code;
        }
        public bool JoinRoom(string code, Client client)
        {
            if (!_activeRooms.TryGetValue(code, out var room))
                return false; // Комната не найдена

            if (room.Participants.Any(p => p.Id == client.Id))
                return true; // Уже подключен

            room.Participants.Add(client);
            client.CurrentRoom = room; // ЗАМЕНИТЬ НА ОТДЕЛЬНЫЙ МЕТОД
            return true;
        }
        public void RemoveRoom(string code)
        {
            _activeRooms.TryRemove(code, out _);
        }

        public Server(int port)
        {
            _port = port;
            
            _httpListener = new HttpListener();
        }

        public async Task StartAsync()
        {
            _httpListener.Prefixes.Add($"http://*:{_port}/");
            Console.WriteLine($"Сервер запущен на порту {_port}...");
            _httpListener.Start();

            while (true)
            {
                var context = await _httpListener.GetContextAsync();

                if (string.IsNullOrEmpty(context.Request.QueryString["uid"])) // || !IsValidToken(token))
                {
                    context.Response.StatusCode = 401; // Unauthorized
                    context.Response.Close();
                    continue;
                }
                
                if (context.Request.IsWebSocketRequest)
                {
                    Client client = new();
                    client.Id = context.Request.QueryString["uid"];
                    client.Username = context.Request.QueryString["name"];
                    _clients.Add(client);

                    var webSocketContext = await context.AcceptWebSocketAsync(null);
                    client.WebSocket = webSocketContext.WebSocket;
                    Console.WriteLine($"Подключение с {client.Username}#{client.Id} установлено");
                    _ = HandleWebSocketClientAsync(client);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }
        private void InitClientRTCPeer(Client client)
        {
            client.Peer = new RTCPeerConnection();
            client.AudioStreams = new();
            client.Peer.oniceconnectionstatechange += (state) =>
            {
                Console.WriteLine("Обновлено состояние RTC: " + state.ToString());
                if (state == RTCIceConnectionState.failed)
                {
                    client.Peer.Dispose();
                    client.WebSocket.Dispose();
                    _clients.Remove(client);
                }
            };

            client.Peer.ondatachannel += (dataChannel) =>
            {
                Console.WriteLine("DataChannel created");
                dataChannel.onmessage += (RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data) =>
                {
                    Console.WriteLine("Message received");
                    string room = Encoding.UTF8.GetString(data, 0, 6);
                    string text = Encoding.UTF8.GetString(data, 6, data.Length - 6);

                    
                    foreach (Client p in _activeRooms[room].Participants)
                    {
                        if (p != client)
                        {
                            p.DataChannel.send($"{client.Username}: {text}");
                            Console.WriteLine($"{p.Username} got {client.Username}: {text} in room {room}");
                        }
                    }

                    Console.WriteLine($"{client.Username}-{room}: {text}");
                    
                };
                client.DataChannel = dataChannel;
                client.DataChannel.onclose += () =>
                {
                    Console.WriteLine("DC CLOSED WTF");
                };
                client.DataChannel.onerror += (string err) =>
                {
                    Console.WriteLine("DC ERROR: " + err);
                };
                client.DataChannel.onopen += () =>
                {
                    Console.WriteLine("DC OPEN");
                };
            };
            client.Peer.onnegotiationneeded += () =>
            {
                Console.WriteLine("RENEGOTIATION WHERE");
            };
            client.MediaStreamTrack = new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMA);
            client.MediaStreamTrack.IsRemote = true;
            client.Peer.AudioStream.LocalTrack = client.MediaStreamTrack; 
            client.Peer.AudioStream.OnRtpPacketReceivedByIndex += (i, ip, t, p) =>
            {
                if (client.CurrentRoom != null)
                {
                    if (t == SDPMediaTypesEnum.audio)
                    {
                        foreach (Client c in client.CurrentRoom.Participants)
                        {
                            if (c != client)
                            {
                                Console.WriteLine(client.Username + " -> " + c.Username);

                                //c.Peer.SendAudio(1, p.Payload);
                                //Console.WriteLine($"{client.Username}-{client.MediaStreamTrack.Timestamp}");
                                c.AudioStreams[client].SendAudio(1, p.Payload);
                            }
                        }
                    }
                }
            };
            client.Peer.addTrack(client.MediaStreamTrack);
            Console.WriteLine("Renegtiation needed: " + client.Peer.RequireRenegotiation);
            Console.WriteLine($"MediaStreamTrack status: {client.MediaStreamTrack.StreamStatus}");
            
        }

        private RTCSessionDescriptionInit ClientRTCAnswer(Client client, RTCSessionDescriptionInit offer)
        {
            client.Peer.setRemoteDescription(offer); //Инкапсулировать внутрь клиента!
            RTCSessionDescriptionInit answer = client.Peer.createAnswer();
            client.Peer.setLocalDescription(answer);
            return answer;
        }
        
        private async Task HandleWebSocketClientAsync(Client client)
        {
            var buffer = new byte[2048];
            WebSocket webSocket = client.WebSocket;
            InitClientRTCPeer(client);
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Закрыто", CancellationToken.None);
                        Console.WriteLine($"Соединение с клиентом {client.Username}#{client.Id} закрыто");

                        client.Peer.Dispose();
                        client.WebSocket.Dispose();
                        _clients.Remove(client);

                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"Сервер получил: {message} от {client.Username}#{client.Id}");

                    ClientRequest request = JsonSerializer.Deserialize<ClientRequest>(message);
                    string roomCode;
                    Room room;
                    ServerResponse response;

                    switch (request.Action)
                    {
                        case RequestType.CreateRoom: //Создание комнаты

                            roomCode = CreateRoom(client.Id);
                            Console.WriteLine($"Создана комната {roomCode}");
                            JoinRoom(roomCode, client);

                            response = new();
                            response.Action = ResponseType.RoomCreated;
                            response.Data = roomCode;
                            SendResponse(webSocket, response);
                            break;

                        case RequestType.JoinRoom: //Присоединение к комнате
                            roomCode = request.Data["RoomCode"];
                            bool roomExists = JoinRoom(roomCode, client);
                            if (roomExists)
                            {
                                _activeRooms.TryGetValue(roomCode, out room);
                                Console.WriteLine($"Пользователь {client.Username} подключился к комнате {roomCode}");
                                Console.WriteLine("Список пользователей в комнате:");
                                foreach (Client p in room.Participants)
                                {
                                    Console.WriteLine(p.Username);
                                }

                                response = new();
                                response.Action = ResponseType.NewRoomParticipant;
                                response.Data = client.Username;
                                foreach (Client p in room.Participants)
                                {
                                    if (client != p)
                                    {
                                        SendResponse(p.WebSocket, response);
                                        var track = new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMA);
                                        track.IsRemote = false; //??
                                        p.Peer.addTrack(track);
                                        p.AudioStreams.Add(client, p.Peer.AudioStreamList.Last());
                                        Console.WriteLine(p.AudioStreams.Values.Count);

                                        track = new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMA);
                                        track.IsRemote = false; //??
                                        client.Peer.addTrack(track);
                                        client.AudioStreams.Add(p, client.Peer.AudioStreamList.Last());
                                    }
                                }
                            }
                            break;
                        
                        case RequestType.LeaveRoom:
                            roomCode = request.Data["RoomCode"];
                            _activeRooms.TryGetValue(roomCode, out room);

                            Console.WriteLine(client.DataChannel.IsOpened);
                            client.DataChannel.close();
                            Console.WriteLine(client.DataChannel.IsOpened);

                            if (room.Participants.Contains(client))
                            {
                                room.Participants.Remove(client);
                                Console.WriteLine($"Пользователь {client.Username} покинул комнату {roomCode}");
                                Console.WriteLine("Список пользователей в комнате:");
                                if (room.Participants.Count == 0)
                                {
                                    _activeRooms.TryRemove(roomCode, out room);
                                    Console.WriteLine($"Комната {roomCode} удалена");
                                    break;
                                }
                                foreach (Client p in room.Participants)
                                {
                                    Console.WriteLine(p.Username);
                                }
                                response = new();
                                response.Action = ResponseType.RoomParticipantLeft;
                                response.Data = client.Username;
                                foreach (Client p in room.Participants)
                                {
                                    SendResponse(p.WebSocket, response);
                                }
                                
                            }
                            break;

                        case RequestType.SendOffer:
                            RTCSessionDescriptionInit offer;
                            RTCSessionDescriptionInit.TryParse(request.Data["Offer"], out offer);
                            RTCSessionDescriptionInit answer = ClientRTCAnswer(client, offer);
                            
                            response = new();
                            response.Action = ResponseType.ResponceAnswer;
                            response.Data = answer.toJSON();
                            SendResponse(webSocket, response); 
                            break;

                        case RequestType.SendIce:
                            RTCIceCandidateInit init;
                            RTCIceCandidateInit.TryParse(request.Data["Ice"], out init);
                            client.Peer.addIceCandidate(init);

                            client.Peer.onicecandidate += (candidate) =>
                            {
                                response = new();
                                response.Action = ResponseType.ResponceIce;
                                response.Data = candidate.toJSON();
                                SendResponse(webSocket, response);
                            };
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
            finally
            {
                client.WebSocket.Dispose();
            }
        }

        

        async void SendResponse(WebSocket webSocket, ServerResponse response)
        {
            var responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));
            await webSocket.SendAsync(
                new ArraySegment<byte>(responseBytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }

    }
    
    public class Client
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public Room CurrentRoom { get; set; }
        public WebSocket WebSocket { get; set; }
        public RTCPeerConnection Peer { get; set; }
        public RTCDataChannel DataChannel { get; set; }
        public MediaStreamTrack MediaStreamTrack { get; set; }
        public Dictionary<Client, AudioStream> AudioStreams { get; set; }

    }
    public class Room
    {
        public string Code { get; set; }          // 6-значный код
        public string OwnerId { get; set; }       // ID создателя
        public List<Client> Participants { get; } = new(); // Список участников
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
    }

    public class ClientRequest
    {
        [JsonPropertyName("action")]
        public RequestType Action { get; set; }
        [JsonPropertyName("data")]
        public Dictionary<string, string> Data { get; set; }
    }
    public class ServerResponse
    {
        [JsonPropertyName("action")]
        public ResponseType Action { get; set; }
        public string Data {  get; set; }
    }

    public enum RequestType
    {
        CreateRoom,
        JoinRoom,
        SendMessage,
        LeaveRoom,
        SendIce,
        SendOffer
    }
    public enum RequestDataType
    {
        RoomCode,
        MessageText
    }
    public enum ResponseType
    {
        RoomCreated,
        NewRoomParticipant,
        Message,
        RoomParticipantLeft,
        ResponceAnswer,
        ResponceIce
    }
    
}