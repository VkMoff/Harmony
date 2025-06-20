using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Harmony_0_2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Core _core = new();
        private Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        
        public MainWindow()
        {
            InitializeComponent();
            _roomsListBox.ItemsSource = _core.ActiveRooms;
            
        }

        private void connectButton_Click(object sender, RoutedEventArgs e)
        {
            _core.ConnectToServer(ipTextBox.Text, int.Parse(portTextBox.Text), usernameTextBox.Text);
            _core.MessageReceivedFromServer += PrintMessage;
        }
        private void PrintMessage(string msg)
        {
            _dispatcher.Invoke(new Action(() =>
            {
                chatTextBox.Text += msg + "\r\n";
            }));
        }
        private void disconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _core.Disconnect();
        }

        private void createRoomButton_Click(object sender, RoutedEventArgs e)
        {
            _core.CreateRoom();
        }

        private void joinRoomButton_Click(object sender, RoutedEventArgs e)
        {
            _core.JoinRoom(codeTextBox.Text);
        }

        private void leaveRoomButton_Click(object sender, RoutedEventArgs e)
        {
            _core.LeaveRoom(codeTextBox.Text);
        }
        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _core.SendMessage(messageTextBox.Text, _roomsListBox.SelectedValue.ToString());
                messageTextBox.Text = String.Empty;
            }
            catch (Exception ex)
            {
                Debug.Print(ex.Message);
            }
        }
        private void voiceButton_Click(object sender, RoutedEventArgs e)
        {
            _core.ConnectToVoiceChat();
        }
    }
}