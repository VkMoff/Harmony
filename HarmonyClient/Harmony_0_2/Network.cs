using System.Diagnostics;
using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
namespace Harmony_0_2
{
    internal class Network
    {
        private ClientWebSocket _webSocket = new ClientWebSocket();
        private Uri _serverUri;
        private string _uid;
        private string _username;
        RTCPeerConnection peerConnection;
        RTCDataChannel dataChannel;
        public event Action<string> MessageReceived;
        public event Action<string> NewRoom;
        public Action<int, byte[]> AudioOutAvailable;

        public void SetUserName(string username)
        {
            _username = username;
        }

        public void InitializeWebSocketServer(IPEndPoint endPoint)
        {
            _uid = Guid.NewGuid().ToString();
            _serverUri = new Uri($"ws://{endPoint.Address}:{endPoint.Port}/?uid={_uid}&name={_username}");
        }
        public void InitializeWebSocketServer(string ip, int port)
        {
            _uid = Guid.NewGuid().ToString();
            _serverUri = new Uri($"ws://{ip}:{port}/?uid={_uid}&name={_username}");
        }
        public async Task ConnectWebSocketAsync()
        {
            await _webSocket.ConnectAsync(_serverUri, CancellationToken.None);
            Debug.Print("Подключено к серверу!");
            _ = ListenForMessagesAsync();
        }

        public async void Close()
        {
            await DisconnectAsync();
        }


        public async Task SendRequestAsync(ClientRequest request)
        {
            string message = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );
        }

        

        private async Task ListenForMessagesAsync()
        {
            var buffer = new byte[2048];
            try
            {
                while (_webSocket.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Debug.Print($"Клиент получил: {message}");

                    ServerResponse response = JsonSerializer.Deserialize<ServerResponse>(message);
                    switch (response.Action)
                    {
                        case ResponseType.RoomCreated:
                            MessageReceived?.Invoke($"Комната {response.Data} создана");
                            NewRoom.Invoke(response.Data);
                            break;
                        case ResponseType.NewRoomParticipant:
                            MessageReceived?.Invoke($"{response.Data} вошёл в чат");

                            MediaStreamTrack audioTrack = new(SDPWellKnownMediaFormatsEnum.PCMA); //Настроить типы форматов!!!
                            audioTrack.IsRemote = true;
                            peerConnection.addTrack(audioTrack);
                            peerConnection.AudioStreamList.Last().OnRtpPacketReceivedByIndex += (i, ip, t, p) =>
                            {
                                AudioOutAvailable.Invoke(i, p.Payload);
                            };

                            break;
                        case ResponseType.RoomParticipantLeft:
                            MessageReceived?.Invoke($"{response.Data} покинул чат");
                            break;
                        case ResponseType.ResponceAnswer:
                            RTCSessionDescriptionInit answer;
                            RTCSessionDescriptionInit.TryParse(response.Data, out answer);
                            peerConnection.setRemoteDescription(answer);
                            break;
                        case ResponseType.ResponceIce:
                            RTCIceCandidateInit ice;
                            RTCIceCandidateInit.TryParse(response.Data, out ice);
                            peerConnection.addIceCandidate(ice);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"Ошибка: {ex.Message}");
            }
        }

        public async Task DisconnectAsync()
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Отключение", CancellationToken.None);
            peerConnection.Close("Peer left session");
            peerConnection.Dispose();
        }

        public async Task InitializeRTC()
        {
            RTCConfiguration config = new RTCConfiguration();
            config.iceServers = new List<RTCIceServer> { new() { urls = "stun:stun.l.google.com:19302" } };
            peerConnection = new RTCPeerConnection(config);

            peerConnection.onicecandidate += (candidate) =>
            {
                ClientRequest request = new ClientRequest();
                request.Action = RequestType.SendIce;
                request.Data = new() { { "Ice", candidate.toJSON() } };

                SendRequestAsync(request);
            }; //ICE
            dataChannel = peerConnection.createDataChannel("chat").Result; //DataChannel
            dataChannel.onmessage += DataChannel_onmessage;
            dataChannel.onopen += () =>
            {
                Debug.Print("DC OPEN");
            };
            peerConnection.oniceconnectionstatechange += (state) =>
            {
                Debug.Print(state.ToString());
            };
            MediaStreamTrack audioTrack = new(SDPWellKnownMediaFormatsEnum.PCMA);
            peerConnection.AudioStream.LocalTrack = audioTrack;
            peerConnection.AudioStream.OnRtpPacketReceivedByIndex += (i, ip, t, p) =>
            {
                AudioOutAvailable.Invoke(i, p.Payload);
            };
            peerConnection.onnegotiationneeded += Renegotiate;
            Renegotiate();
        }
        public void Renegotiate()
        {
            
            Debug.Print("RENEGOTIATION");
            RTCSessionDescriptionInit offer = peerConnection.createOffer(); //Offer

            peerConnection.setLocalDescription(offer);
            ClientRequest request = new ClientRequest();
            request.Action = RequestType.SendOffer;
            request.Data = new() { { "Offer", offer.toJSON() } };
            SendRequestAsync(request);
            
        }
        private void DataChannel_onmessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
        {
            Debug.Print(Encoding.UTF8.GetString(data));
            MessageReceived.Invoke(Encoding.UTF8.GetString(data));
        }

        public void SendChatMessage(string message)
        {
            dataChannel.send(message);
            MessageReceived?.Invoke("Вы: " + message.Substring(6));
        }

        public void SendAudio(byte[] data, int bytes)
        {
            peerConnection.SendAudio(1, data);
            Debug.Print("AUDIO SENT");
        }
    }
    public class ServerResponse
    {
        [JsonPropertyName("action")]
        public ResponseType Action { get; set; }
        public string Data { get; set; }
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
