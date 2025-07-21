using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;


namespace MachineEar_MIC
{
    public enum ProtocolName
    {
        Connect,        // 0-0-0
        StatusCheck,    // 0-0-1
        AudioSend,      // 0-1-0
        DeviceStatus    // 0-2-0
    }

    /// Interaction logic for MainWindow.xaml
    public partial class MainWindow : Window
    {
        private TcpClientService tcpService;
        private const int AUDIO_SEND_PERIOD_SEC = 5;
        private System.Timers.Timer wavTimer;
        private System.Timers.Timer micTimer;
        private WaveInEvent waveIn;
        private MemoryStream audioBuffer;

        private WaveInEvent waveIn;
        private List<float> audioBuffer = new();
        private DispatcherTimer plotTimer;
        private int selectedDeviceIndex = 0;
        private const int SampleRate = 44100;

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
                // MAC 주소 랜덤 생성
                string mac = GenerateRandomMac();
                var config = new IPAddress_Local { MAC = mac, IP = "", PORT = 0 };
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(path_server, json);
            }

            string read = File.ReadAllText(path_server);
            this.iPAddress = JsonConvert.DeserializeObject<IPAddress_Local>(read);
        }

        private void comboDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedDeviceIndex = comboDevices.SelectedIndex;
            waveIn?.StopRecording();
            waveIn?.Dispose();
            audioBuffer.Clear();
            InitMic(selectedDeviceIndex);
        }

        private void InitMic(int deviceIndex)
        {
            private Socket _clientSocket;
            private CancellationTokenSource _receiveCts;
            private const int BufferSize = 8192;

            plotTimer = new DispatcherTimer();
            plotTimer.Interval = TimeSpan.FromMilliseconds(50); // 20fps
            plotTimer.Tick += PlotTimer_Tick;
            plotTimer.Start();
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                if (IsConnected)
                {
                    Debug.WriteLine("이미 서버에 연결되어 있습니다.");
                    return true;
                }

                try
                {
                    Debug.WriteLine("[ConnectAsync] 시작");

                    _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);

                    Debug.WriteLine($"서버에 연결 중... ({ipAddress}:{port})");
                    await _clientSocket.ConnectAsync(serverEndPoint);

                    Debug.WriteLine("서버에 성공적으로 연결되었습니다!");
                    OnConnectionStatusChanged(true);

                    // JSON 전송
                    var connectJson = new Dictionary<string, string>
                    {
                        { "PROTOCOL", "0-0-0" },
                        { "MAC", macAddress }
                        //{ "MAC", "MAC" }
                        
                    };
                    string jsonStr = JsonConvert.SerializeObject(connectJson);
                    Debug.WriteLine(jsonStr);
                    await SendMessageAsync(jsonStr);

                    // 수신 루프 시작
                    _receiveCts = new CancellationTokenSource();
                    _ = Task.Run(() => ReceiveDataAsync(_receiveCts.Token));
                    return true;
                }
                catch (Exception ex)
                {
                    OnErrorOccurred($"연결 오류: {ex.Message}");
                }

                return false;
            }

            /// <summary>
            /// 서버로부터 데이터를 비동기적으로 수신하는 루프입니다.
            /// </summary>
            private async Task ReceiveDataAsync(CancellationToken cancellationToken)
            {
                Debug.WriteLine("ReceiveDataAsync 진입");

                // byte[] buffer = new byte[BufferSize]; // 이 임시 버퍼 대신 클래스 멤버인 _receiveBuffer 사용
                try
                {
                    while (!cancellationToken.IsCancellationRequested && IsConnected)
                    {
                        // ✅ 여기서 수신 대기 로그 추가
                        Debug.WriteLine("👉 [ReceiveAsync 대기 시작]");

            // 파형 표시
            var plotSamples = audioBuffer.Skip(Math.Max(0, audioBuffer.Count - 2000)).ToArray();
            wpfPlot.Plot.Clear();
            wpfPlot.Plot.Add.Signal(plotSamples, color: Colors.DodgerBlue);
            wpfPlot.Plot.Axes.AutoScale();
            wpfPlot.RenderSize = new System.Windows.Size(800, 300);

                        Debug.WriteLine($"✅ [수신 완료] bytesRead={bytesRead}");

            // FFT 표시
            int fftLen = 2048;
            if (plotSamples.Length < fftLen) return;
            var fftInput = plotSamples.Skip(plotSamples.Length - fftLen).Take(fftLen).Select(x => new Complex(x, 0)).ToArray();
            
            //Fourier.Forward(fftInput, FourierOptions.Matlab);
            double[] fftMag = fftInput.Take(fftLen / 2).Select(c => c.Magnitude).ToArray();
            double[] freq = Enumerable.Range(0, fftLen / 2).Select(i => i * SampleRate / (double)fftLen).ToArray();

            wpfPlotFFT.Plot.Clear();
            wpfPlotFFT.Plot.Add.Scatter(freq, fftMag, color: Colors.MediumVioletRed);
            wpfPlotFFT.Plot.Title("FFT (주파수 스펙트럼)");
            wpfPlotFFT.Plot.XLabel("Frequency (Hz)");
            wpfPlotFFT.Plot.YLabel("Magnitude");
            wpfPlotFFT.Plot.Axes.AutoScale();
            wpfPlotFFT.RenderSize = new System.Windows.Size(800, 300);
        }

            // 수신부
            private void ProcessReceivedData()
            {
                Debug.WriteLine("📩 [ProcessReceivedData 진입]");

                // _currentMessageBuffer의 현재 위치를 처음으로 설정하여 읽을 수 있도록 함
                _currentMessageBuffer.Position = 0;

                // 데이터를 모두 읽고 처리할 때까지 반복
                while (true)
                {
                    switch (_currentState)
                    {
                        case ReceiveState.WaitingForHeader:
                            // 헤더(8바이트)를 받을 수 있는 충분한 데이터가 있는지 확인
                            //Debug.WriteLine($"[상태: {_currentState}] Position={_currentMessageBuffer.Position}, Length={_currentMessageBuffer.Length}");

                            if (_currentMessageBuffer.Length >= 8)
                            {
                                byte[] headerBytes = new byte[8];
                                _currentMessageBuffer.Read(headerBytes, 0, 8); // 버퍼에서 8바이트 읽기

                                // 바이트 배열을 정수로 변환 (리틀 엔디안 가정)
                                _totalMessageSize = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(headerBytes, 0));
                                _jsonSize = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(headerBytes, 4));

                                // 헤더 유효성 검사 (예: JSON 크기가 전체 크기보다 클 수 없음)
                                if (_jsonSize > _totalMessageSize || _totalMessageSize <= 8) // 최소 크기는 헤더 8바이트 + 최소 JSON/파일 데이터
                                {
                                    OnErrorOccurred($"잘못된 헤더 수신: TotalSize={_totalMessageSize}, JsonSize={_jsonSize}. 연결을 끊습니다.");
                                    Disconnect(); // 잘못된 헤더는 치명적인 오류로 간주하여 연결 끊기
                                    return; // 루프 종료
                                }

                                Debug.WriteLine($"헤더 수신: 전체 크기={_totalMessageSize}, JSON 크기={_jsonSize}");
                                _currentState = ReceiveState.WaitingForJsonData; // 다음 상태로 전환
                            }
                            else
                            {
                                return;
                            }
                            break;

                        case ReceiveState.WaitingForJsonData:
                            // JSON 데이터를 받을 수 있는 충분한 데이터가 있는지 확인
                            Debug.WriteLine($"[상태: {_currentState}] Position={_currentMessageBuffer.Position}, Length={_currentMessageBuffer.Length}");

                            if (_currentMessageBuffer.Length - _currentMessageBuffer.Position >= _jsonSize)
                            {
                                byte[] jsonBytes = new byte[_jsonSize];
                                _currentMessageBuffer.Read(jsonBytes, 0, _jsonSize); // JSON 크기만큼 데이터 읽기

                                string jsonData = Encoding.UTF8.GetString(jsonBytes);
                                Debug.WriteLine($"JSON 데이터 수신: {jsonData}");

                                // TODO: 여기서 JSON 데이터를 역직렬화하고 처리하는 로직 추가
                                // 예: var myObject = JsonSerializer.Deserialize<MyMessageType>(jsonData);

                                _currentState = ReceiveState.WaitingForFileData; // 다음 상태로 전환
                            }
                            else
                            {
                                // JSON 데이터를 읽을 데이터가 부족하면 루프 종료
                                return;
                            }
                            break;

                        case ReceiveState.WaitingForFileData:
                            // 파일 데이터 크기 계산
                            Debug.WriteLine($"[상태: {_currentState}] Position={_currentMessageBuffer.Position}, Length={_currentMessageBuffer.Length}");

                            int fileDataSize = _totalMessageSize - _jsonSize; 
                            if (fileDataSize < 0) // 예외 처리
                            {
                                OnErrorOccurred($"파일 데이터 크기 계산 오류: FileDataSize={fileDataSize}. 연결을 끊습니다.");
                                Disconnect();
                                return;
                            }

                            // 파일 데이터를 받을 수 있는 충분한 데이터가 있는지 확인
                            if (_currentMessageBuffer.Length - _currentMessageBuffer.Position >= fileDataSize)
                            {
                                byte[] fileDataBytes = new byte[fileDataSize];
                                _currentMessageBuffer.Read(fileDataBytes, 0, fileDataSize); // 파일 크기만큼 데이터 읽기

                                Debug.WriteLine($"파일 데이터 수신: {fileDataSize} 바이트");
                                // TODO: 여기서 파일 데이터를 처리하는 로직 추가
                                // 예: File.WriteAllBytes("received_file.bin", fileDataBytes);

                                // 한 메시지 처리가 완료되었으므로 버퍼 정리 및 상태 초기화
                                // 남은 데이터를 다음 메시지의 시작으로 옮기기
                                CleanupMessageBuffer();

                                _currentState = ReceiveState.WaitingForHeader; // 다음 메시지를 위해 상태 초기화
                                _totalMessageSize = 0;
                                _jsonSize = 0;
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