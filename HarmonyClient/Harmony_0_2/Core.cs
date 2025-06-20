using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
namespace Harmony_0_2
{
    internal class Core
    {
        private Network _network = new Network();
        private AudioServices _audioServices = new AudioServices();
        private ObservableCollection<string> _activeRooms = new ObservableCollection<string>();
        public ObservableCollection<string> ActiveRooms { get { return _activeRooms; } }
        public event Action<string> MessageReceivedFromServer;
        public async void ConnectToServer(string ip, int port, string username)
        {
            _network.SetUserName(username);
            _network.InitializeWebSocketServer(ip, port);
            await _network.ConnectWebSocketAsync();
            _network.MessageReceived += _network_MessageReceived;
            _network.NewRoom += (roomCode) =>
            {
                _activeRooms.Add(roomCode);
            };
            await _network.InitializeRTC();
            _audioServices.StartCapturing();
            _audioServices.StartPlaying();

            _audioServices.AudioInAvailable += (data, bytes) =>
            {
                _network.SendAudio(data, bytes);
            };
            _network.AudioOutAvailable += (index, data) =>
            {
                _audioServices.PlaySound(data);
                //_audioServices.MixSound(index, data);
            };
        }

        private void _network_MessageReceived(string obj)
        {
            MessageReceivedFromServer?.Invoke(obj);
        }

        public void Disconnect()
        {
            _network.DisconnectAsync();
        }
        public void CreateRoom()
        {
            ClientRequest request = new ClientRequest();
            request.Action = RequestType.CreateRoom;
            _network.SendRequestAsync(request);
        }
        public void JoinRoom(string code)
        {
            ClientRequest request = new ClientRequest();
            request.Action = RequestType.JoinRoom;
            request.Data = new() { { "RoomCode", code.ToString() } };
            _network.SendRequestAsync(request);
            _activeRooms.Add(code);
            _network.Renegotiate();
        }
        public void SendMessage(string text, string room)
        {
            _network.SendChatMessage(room+text);
        }
        public void LeaveRoom(string code)
        {
            ClientRequest request = new();
            request.Action = RequestType.LeaveRoom;
            request.Data = new() { { "RoomCode", code.ToString() } };
            _network.SendRequestAsync(request);
        }
        public void ConnectToVoiceChat()
        {
            //_network.InitializeRTC();
        }
    }
    public class ClientRequest
    {
        [JsonPropertyName("action")]
        public RequestType Action { get; set; }
        [JsonPropertyName("data")]
        public Dictionary<string, string> Data { get; set; }
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
}
