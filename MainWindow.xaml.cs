using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Linq;
using Microsoft.Win32;

namespace MachineEar_MIC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //private const string ServerIp = "10.10.20.111"; // 실제 서버 IP로 변경
        //private const string Port = "5000";

        public MainWindow()
        {
            InitializeComponent();
            //textbox_ip.Text = ServerIp; // IP 주소를 고정된 값으로 설정
            //textbox_port.Text = "5000"; // 기본 포트 번호 설정
        }

        private void connect_btn_click(object sender, RoutedEventArgs e)
        {
            string ip = textbox_ip.Text.Trim();
            string portText = textbox_port.Text.Trim();

            if (int.TryParse(portText, out int port))
            {
                bool connected = TryConnectToIpPort(ip, port);
                if (connected)
                {
                    MessageBox.Show("연결 성공!", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("연결 실패. IP와 포트 번호를 확인하세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("유효하지 않은 포트 번호입니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool IsValidIp(string ip)
        {
            return System.Net.IPAddress.TryParse(ip, out _);
        }

        private bool IsValidPort(string portText, out int port)
        {
            return int.TryParse(portText, out port) && port > 0 && port <= 65535;
        }

        private bool TryConnectToIpPort(string ip, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect(ip, port);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private void btn_mac_connect_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "WAV 파일 선택";
            openFileDialog.Filter = "Wave 파일 (*.wav)|*.wav";

            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFilePath = openFileDialog.FileName;
                float[] samples = ReadWavFileSamples(selectedFilePath);
                DrawWaveform(samples);
            }
        }

        // WAV 파일에서 샘플 추출 (16bit PCM, mono/stereo 지원)
        private float[] ReadWavFileSamples(string filePath)
        {
            using (var reader = new BinaryReader(File.OpenRead(filePath)))
            {
                // WAV 헤더 스킵
                reader.BaseStream.Seek(44, SeekOrigin.Begin);

                var samples = new List<float>();
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    short sample = reader.ReadInt16();
                    samples.Add(sample / 32768f); // 16bit PCM 정규화
                }
                return samples.ToArray();
            }
        }
        // 파형 그리기 (Canvas 필요)
        private void DrawWaveform(float[] samples)
        {
            canvas_waveform.Children.Clear();

            int width = (int)canvas_waveform.ActualWidth;
            int height = (int)canvas_waveform.ActualHeight;
            if (width == 0 || height == 0) { width = 400; height = 100; }

            Polyline polyline = new Polyline
            {
                Stroke = Brushes.Blue,
                StrokeThickness = 1
            };

            int sampleCount = samples.Length;
            int displayCount = width; // 픽셀 수만큼 샘플 표시
            for (int i = 0; i < displayCount; i++)
            {
                int sampleIndex = i * sampleCount / displayCount;
                float sample = samples[sampleIndex];
                double x = i;
                double y = height / 2 - sample * (height / 2 - 2);
                polyline.Points.Add(new System.Windows.Point(x, y));
            }

            canvas_waveform.Children.Add(polyline);
        }

        private void RadioButton_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void TextBox_TextChanged_1(object sender, TextChangedEventArgs e)
        {

        }
    }
}