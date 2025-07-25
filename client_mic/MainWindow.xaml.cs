using Microsoft.VisualBasic;
using Microsoft.Win32;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScottPlot;
using ScottPlot.Finance;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;  // MAC 주소 관련
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml;
using Formatting = Newtonsoft.Json.Formatting;


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

        public MainWindow()
        {
            InitializeComponent();

            // config 파일 경로 설정
            path_server = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            // config 불러오기 (없는 경우 생성 포함)
            load_serveraddr(path_server);

            // UI에 설정값 반영
            textbox_ip.Text = iPAddress.IP;
            textbox_port.Text = iPAddress.PORT.ToString();
            textbox_mac.Text = iPAddress.MAC;

            // 서버 접속 상태
            tcpService = new TcpClientService();
            tcpService.ConnectionStatusChanged += (sender, isConnected) =>
            {
                SetConnectionStatus(isConnected);
            };

            var waveIn = GetWaveIn();
            InitConfig(); // 마이크 설정

        }

        public class IPAddress_Local
        {
            public string MAC { get; set; }
            public string IP { get; set; }
            public int PORT { get; set; }
        }

        private void InitConfig()
        {
            path_server = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            if (!File.Exists(path_server))
            {
                // MAC 주소 랜덤 생성
                string mac = GenerateRandomMac();
                var config = new IPAddress_Local { MAC = mac, IP = "", PORT = 0 };
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(path_server, json);
            }

            if (string.IsNullOrWhiteSpace(iPAddress.MAC))
            {
                iPAddress.MAC = GenerateRandomMac();
                File.WriteAllText(path_server, JsonConvert.SerializeObject(iPAddress, Formatting.Indented));
                Debug.WriteLine("💡 MAC 값이 없어서 새로 생성 후 저장했습니다.");
            }

            string read = File.ReadAllText(path_server);
            this.iPAddress = JsonConvert.DeserializeObject<IPAddress_Local>(read);
        }

        private string GenerateRandomMac()
        {
            Random rand = new Random();
            byte[] mac = new byte[6];
            rand.NextBytes(mac);
            mac[0] = (byte)((mac[0] & 0xFE) | 0x02); // Locally Administered
            return string.Join(":", mac.Select(b => b.ToString("X2")));
        }


        public class TcpClientService : IDisposable
        {
            // 🔹 STATE=0 수신 시 실행할 콜백 등록
            public Action OnForceStopRequested;
            public Action OnForceStartRequested; // 오디오 전송 요청 콜백

            // 서버에서 수신한 NUM_PIN 저장용
            private string _numPin;
            public string NumPin => _numPin;
            public Action<string> OnNumPinReceived; // NUM_PIN이 저장된 후 자동 실행될 콜백

            // 프로토콜 타입에 따라 문자열 반환
            private string GetProtocolCode(ProtocolName type)
            {
                return type switch
                {
                    ProtocolName.Connect => "0-0-0",
                    ProtocolName.StatusCheck => "0-0-1",
                    ProtocolName.AudioSend => "0-1-0",
                    ProtocolName.DeviceStatus => "0-2-0",
                    _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
                };
            }

            // 프로토콜 종류에 따라 JSON 구성
            private string CreateProtocolJson
            (  // 매개변수:
                ProtocolName type,
                string mac = null,
                string pin = null,
                int? size = null,
                int? time = null,
                string source = null,
                string connection = null
            )

            {
                var json = new Dictionary<string, object>
                {
                    { "PROTOCOL", GetProtocolCode(type) }
                };

                switch (type)
                {
                    case ProtocolName.Connect:
                        json["MAC"] = mac;
                        break;

                    case ProtocolName.StatusCheck:
                        json["NUM_PIN"] = pin;
                        break;

                    case ProtocolName.AudioSend:
                        json["NUM_PIN"] = pin;
                        json["__META__"] = new Dictionary<string, object>
                        {
                            { "SIZE", size ?? 0 },
                            { "SAMPLING_RATE", 16000 }, // 16KHz,
                            { "TIME", 10 },
                            { "SOURCE", source ?? "WAV" }
                        };
                        break;

                    case ProtocolName.DeviceStatus:
                        json["NUM_PIN"] = pin;
                        json["TYPE"] = "MIC";
                        json["CONNECTION"] = connection ?? "Normal";
                        json["LASTTIME"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        break;
                }

                return JsonConvert.SerializeObject(json, Formatting.Indented);
            }

            // 최종 전송 진입 함수 (유연한 JSON 생성 후 전송)
            public async Task<bool> SendProtocolAsync
            (  // 매개변수:
                ProtocolName type,
                string mac = null,
                string pin = null,
                int? size = null,
                int? time = null,
                string source = null,
                string connection = null,
                byte[] fileData = null
            )
            {
                try
                {
                    string json = CreateProtocolJson(type, mac, pin, size, time, source, connection);
                    return await SendMessageAsync(json, fileData);
                }
                catch (Exception ex)
                {
                    OnErrorOccurred($"SendProtocolAsync 오류: {ex.Message}");
                    return false;
                }
            }

            // TCP 클라이언트 소켓
            private Socket _clientSocket;
            private CancellationTokenSource _receiveCts;
            private const int BufferSize = 8192;

            public event EventHandler<string> MessageReceived;
            public event EventHandler<string> ErrorOccurred;
            public event EventHandler<bool> ConnectionStatusChanged;


            public bool IsConnected => _clientSocket != null && _clientSocket.Connected;

            private byte[] _receiveBuffer = new byte[8192]; // 소켓에서 직접 읽어올 임시 버퍼
            private MemoryStream _currentMessageBuffer = new MemoryStream(); // 현재 메시지를 구성하는 데이터를 축적할 버퍼

            // 메시지 파싱 상태를 나타내는 열거형
            private enum ReceiveState
            {
                WaitingForHeader,
                WaitingForJsonData,
                WaitingForFileData
            }

            private ReceiveState _currentState = ReceiveState.WaitingForHeader; // 현재 수신 상태
            private int _totalMessageSize = 0; // 헤더에서 읽은 전체 메시지 크기
            private int _jsonSize = 0;       // 헤더에서 읽은 JSON 데이터 크기
            private object text_connectStatus;

            public TcpClientService()
            {
                // 생성자에서는 특별한 초기화 없이 이벤트만 정의
            }

            public object GetText_connectStatus()
            {
                return text_connectStatus;
            }

            public async Task<bool> ConnectAsync(string ipAddress, int port, string macAddress, object text_connectStatus)
            {
                if (IsConnected)
                {
                    Debug.WriteLine("이미 서버에 연결되어 있습니다.");
                    return true;
                }

                try
                {
                    Debug.WriteLine("[ConnectAsync] 시작");

                    this._clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);

                    Debug.WriteLine($"서버에 연결 중... ({ipAddress}:{port})");
                    await _clientSocket.ConnectAsync(serverEndPoint);

                    Debug.WriteLine("서버에 성공적으로 연결되었습니다!");
                    OnConnectionStatusChanged(true);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        //text_connectStatus.Text = "Connected";
                    });


                    // ✅ 리팩토링된 전송 방식
                    await SendProtocolAsync(ProtocolName.Connect, mac: macAddress);

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
                //Debug.WriteLine("ReceiveDataAsync 진입");

                // byte[] buffer = new byte[BufferSize]; // 이 임시 버퍼 대신 클래스 멤버인 _receiveBuffer 사용
                try
                {
                    while (!cancellationToken.IsCancellationRequested && IsConnected)
                    {
                        // ✅ 수신 대기 로그
                        //Debug.WriteLine("👉 [ReceiveAsync 대기 시작]");

                        int bytesRead = await _clientSocket.ReceiveAsync(
                            new ArraySegment<byte>(_receiveBuffer),
                            SocketFlags.None
                        );

                        //Debug.WriteLine($"✅ [수신 완료] bytesRead={bytesRead}");

                        if (bytesRead == 0)
                        {
                            Debug.WriteLine("서버에서 연결을 종료했습니다.");
                            break;
                        }

                        // 받은 데이터를 현재 메시지 버퍼에 추가
                        _currentMessageBuffer.Write(_receiveBuffer, 0, bytesRead);

                        // 받은 데이터가 있을 때마다 메시지를 파싱 시도
                        ProcessReceivedData();
                    }
                }
                // ... (기존 예외 처리 로직은 동일)
                catch (ObjectDisposedException)
                {
                    Debug.WriteLine("수신 소켓이 닫혔습니다.");
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.ConnectionReset || ex.SocketErrorCode == SocketError.Interrupted)
                    {
                        OnErrorOccurred("서버와의 연결이 강제로 끊어졌습니다.");
                    }
                    else
                    {
                        OnErrorOccurred($"수신 오류: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    OnErrorOccurred($"예기치 않은 수신 오류: {ex.Message}");
                }
                finally
                {
                    if (IsConnected)
                    {
                        Debug.WriteLine("수신 루프 종료. 연결을 끊습니다.");
                        Disconnect();
                    }
                    _currentMessageBuffer.Dispose(); // MemoryStream 자원 해제
                }
            }

            //////////////////수신부//////////////////
            private void ProcessReceivedData()
            {
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

                                //Debug.WriteLine($"헤더 수신: 전체 크기={_totalMessageSize}, JSON 크기={_jsonSize}");
                                _currentState = ReceiveState.WaitingForJsonData; // 다음 상태로 전환
                            }
                            else
                            {
                                return;
                            }
                            break;

                        case ReceiveState.WaitingForJsonData:
                            // JSON 데이터를 받을 수 있는 충분한 데이터가 있는지 확인
                            //Debug.WriteLine($"[상태: {_currentState}] Position={_currentMessageBuffer.Position}, Length={_currentMessageBuffer.Length}");

                            if (_currentMessageBuffer.Length - _currentMessageBuffer.Position >= _jsonSize)
                            {
                                byte[] jsonBytes = new byte[_jsonSize];
                                _currentMessageBuffer.Read(jsonBytes, 0, _jsonSize); // JSON 크기만큼 데이터 읽기

                                // JSON 데이터 수신: jsonData
                                string jsonData = Encoding.UTF8.GetString(jsonBytes);
                                Debug.WriteLine($"JSON 데이터 수신: {jsonData}");

                                try
                                {
                                    var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);

                                    if (parsed.ContainsKey("PROTOCOL") && parsed["PROTOCOL"].ToString() == "0-0-0")
                                    {
                                        if (parsed.ContainsKey("RESPONSE") && parsed["RESPONSE"].ToString() == "OK")
                                        {
                                            // ✅ 서버가 반환한 NUM_PIN 저장
                                            if (parsed.ContainsKey("NUM_PIN"))
                                            {
                                                _numPin = parsed["NUM_PIN"].ToString();
                                                Debug.WriteLine($"[NUM_PIN 저장됨] {_numPin}");
                                            }
                                        }
                                        else if (parsed.ContainsKey("RESPONSE") && parsed["RESPONSE"].ToString() == "NO")
                                        {
                                            Debug.WriteLine("서버 연결 응답이 ERROR입니다.");
                                            OnErrorOccurred("서버 연결 응답이 ERROR입니다.");
                                            // 아무것도 못하고 STATE_ACTIVE=True 올때까지 기다려야됨
                                        }

                                        else
                                        {
                                            Debug.WriteLine("서버 연결 응답이 OK가 아닙니다.");
                                            OnErrorOccurred("서버 연결 응답이 OK가 아닙니다.");
                                        }
                                    }

                                    if (parsed.ContainsKey("NUM_PIN")) // 수정
                                    {
                                        _numPin = parsed["NUM_PIN"].ToString();
                                        //Debug.WriteLine($"[NUM_PIN 저장됨] {_numPin}");

                                        // ✅ NUM_PIN 저장 후 콜백 호출
                                        OnNumPinReceived?.Invoke(_numPin);
                                    }

                                    if (parsed.ContainsKey("STATE_ACTIVE"))
                                    {
                                        if((bool)parsed["STATE_ACTIVE"] == false)
                                        {
                                            Debug.WriteLine("서버로부터 STATE=0 신호 수신. 전송 중단 요청 콜백 실행");
                                            // 🔸 MainWindow에서 등록한 콜백 실행
                                            OnForceStopRequested?.Invoke();
                                        }
                                        else
                                        {
                                            Debug.WriteLine("서버로부터 STATE=1 신호 수신. 전송 시작 요청 콜백 실행");
                                            // 🔸 MainWindow에서 등록한 콜백 실행
                                            OnForceStartRequested?.Invoke();
                                        }
                                    }

                                    // 이후 다른 응답 처리도 필요 시 여기에 확장

                                }

                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"❌ JSON 파싱 오류: {ex.Message}");
                                }

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
                            //Debug.WriteLine($"[상태: {_currentState}] Position={_currentMessageBuffer.Position}, Length={_currentMessageBuffer.Length}");

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
                                // 파일 데이터를 읽을 데이터가 부족하면 루프 종료
                                return;
                            }
                            break;
                    }
                }
            }

            private void CleanupMessageBuffer()
            {
                // 현재 읽기 위치(Position)부터 끝까지 남은 데이터의 길이
                long remainingLength = _currentMessageBuffer.Length - _currentMessageBuffer.Position;

                if (remainingLength > 0)
                {
                    // 남은 데이터를 새 버퍼로 옮김
                    byte[] remainingBytes = new byte[remainingLength];
                    _currentMessageBuffer.Read(remainingBytes, 0, (int)remainingLength);

                    // 기존 스트림을 재설정하고 남은 데이터를 다시 씀
                    _currentMessageBuffer.SetLength(0); // 스트림 길이 0으로 리셋
                    _currentMessageBuffer.Position = 0; // ⬅️ 중요!

                    _currentMessageBuffer.Write(remainingBytes, 0, remainingBytes.Length);
                    _currentMessageBuffer.Position = 0; // ⬅️ 여기 반드시 추가!

                }
                else
                {
                    // 남은 데이터가 없으면 스트림을 완전히 비움
                    _currentMessageBuffer.SetLength(0);
                    _currentMessageBuffer.Position = 0; // 위치도 0으로 재설정
                }
            }

            /// 서버로 메시지를 비동기적으로 전송합니다.
            /// <param name="message">전송할 메시지</param>
            /// <returns>전송 성공 여부</returns>

            /// 송신부
            public async Task<bool> SendMessageAsync(string jsonMessage, byte[] fileData = null)
            {
                //Debug.WriteLine("[SendMessageAsync] 진입");

                if (!IsConnected)
                {
                    OnErrorOccurred("오류: 서버에 연결되어 있지 않습니다.");
                    return false;
                }
                if (string.IsNullOrEmpty(jsonMessage))
                {
                    OnErrorOccurred("오류: 보낼 메시지를 입력하세요.");
                    return false;
                }

                try
                {
                    // JSON 바이트 변환
                    byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonMessage);
                    int jsonSize = jsonBytes.Length;
                    int fileSize = fileData?.Length ?? 0;
                    int totalSize = jsonSize + fileSize;  

                    // [헤더] 4바이트(totalSize) + 4바이트(jsonSize), 네트워크 바이트 오더
                    byte[] header = new byte[8];
                    Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(totalSize)), 0, header, 0, 4);
                    Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(jsonSize)), 0, header, 4, 4);

                    // ✅ [수정] 전체 전송 버퍼 크기 조정: header + json + file
                    byte[] sendBuffer = new byte[8 + jsonSize + fileSize];  // 🔧 수정 지점
                    Array.Copy(header, 0, sendBuffer, 0, 8);
                    Array.Copy(jsonBytes, 0, sendBuffer, 8, jsonSize);

                    // ✅ [추가] 파일이 있을 경우 바이너리 데이터 추가
                    if (fileSize > 0)
                    {
                        Array.Copy(fileData, 0, sendBuffer, 8 + jsonSize, fileSize);  // 🔧 추가 지점
                    }

                    // ✅ [추가] 실제 전송 디버그 로그
                    Debug.WriteLine($"[전송 디버그] totalSize={totalSize}, jsonSize={jsonSize}, fileSize={fileSize}, sendBuffer={sendBuffer.Length}");


                    // 전송
                    await _clientSocket.SendAsync(new ArraySegment<byte>(sendBuffer), SocketFlags.None);

                    Debug.WriteLine($"[전송] 총 {sendBuffer.Length} 바이트 (헤더+JSON)");
                    //Debug.WriteLine($"[전송 JSON] {jsonMessage}");
                    return true;
                }
                catch (SocketException ex)
                {
                    OnErrorOccurred($"전송 오류: {ex.Message}");
                    Disconnect();
                }
                catch (ObjectDisposedException)
                {
                    OnErrorOccurred("오류: 소켓이 이미 닫혔습니다. 다시 연결해주세요.");
                    Disconnect();
                }
                catch (Exception ex)
                {
                    OnErrorOccurred($"예기치 않은 전송 오류: {ex.Message}");
                    Disconnect();
                }

                return false;
            }

            /// 서버와의 연결을 끊고 자원을 해제합니다.
            public void Disconnect()
            {
                // 수신 작업을 취소
                _receiveCts?.Cancel();

                if (_clientSocket != null)
                {
                    try
                    {
                        if (_clientSocket.Connected)
                        {
                            _clientSocket.Shutdown(SocketShutdown.Both);
                        }
                        _clientSocket.Close();
                        _clientSocket.Dispose();
                        _clientSocket = null;
                        Debug.WriteLine("서버와의 연결이 끊어졌습니다.");
                        OnConnectionStatusChanged(false);
                        text_connectStatus = "Disconnected"; // 연결 상태 업데이트
                    }
                    catch (SocketException ex)
                    {
                        OnErrorOccurred($"연결 해제 오류: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred($"예기치 않은 연결 해제 오류: {ex.Message}");
                    }
                }
                _receiveCts?.Dispose();
                _receiveCts = null;
            }

            // 이벤트 발생 도우미 메서드 (null 체크)
            protected virtual void OnConnectionStatusChanged(bool isConnected)
            {
                ConnectionStatusChanged?.Invoke(this, isConnected);
            }

            protected virtual void OnMessageReceived(string message)
            {
                MessageReceived?.Invoke(this, message);
            }

            protected virtual void OnErrorOccurred(string errorMessage)
            {
                ErrorOccurred?.Invoke(this, errorMessage);
            }

            // IDisposable 구현: 객체 소멸 시 자원 해제 보장
            public void Dispose()
            {
                Disconnect(); // Dispose 호출 시 연결 해제
            }
        }




        /// ////////////////////////////////////////////////////////////

        private IPAddress_Local iPAddress = new IPAddress_Local();

        public string path_server { get; private set; }

        private void load_serveraddr(string path_meta)
        {
            try
            {
                if (!File.Exists(path_meta))
                {
                    // MAC 주소가 없는 경우 랜덤 생성
                    string mac = GenerateRandomMac();

                    var initialConfig = new
                    {
                        MAC = mac,
                        IP = "",
                        PORT = 0
                    };
                    File.WriteAllText(path_meta, JsonConvert.SerializeObject(initialConfig, Formatting.Indented));
                    //File.WriteAllText(path_meta, json);
                }

                string read_string = File.ReadAllText(path_meta);
                var read_json = JsonConvert.DeserializeObject<IPAddress_Local>(read_string);

                this.iPAddress = new IPAddress_Local
                {
                    MAC = read_json.MAC,
                    IP = read_json.IP,
                    PORT = read_json.PORT
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show("Config 파일 로딩 실패: " + ex.Message);
            }
        }


        // MAC 및 서버 주소 파일 저장하기
        private void save_serveraddr(string path_meta, string IP, int port)
        {
            Dictionary<string, object> dict_json = new Dictionary<string, object>();

            dict_json.Add("MAC", iPAddress.MAC); // MAC 주소는 이미 iPAddress에 저장되어 있음
            dict_json.Add("IP", IP);
            dict_json.Add("PORT", port);

            string json = JsonConvert.SerializeObject(dict_json);
            File.WriteAllText(path_meta, json);
        }

        private static WaveInEvent GetWaveIn()
        {
            return new NAudio.Wave.WaveInEvent
            {
                DeviceNumber = 0, // 사용할 마이크 장치 인덱스 (0: 기본 장치)
                WaveFormat = new NAudio.Wave.WaveFormat(rate: 1000, bits: 16, channels: 1), // 1kHz, 16비트, Mono
                BufferMilliseconds = 100 // 10ms 단위로 버퍼 처리 (짧을수록 실시간 반응)
            };
        }

        private async void connect_btn_click(object sender, RoutedEventArgs e)
        {
            string ip = textbox_ip.Text.Trim();
            string portText = textbox_port.Text.Trim();

            if (!int.TryParse(portText, out int port))
            {
                MessageBox.Show("유효하지 않은 포트 번호입니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            tcpService = new TcpClientService();

            // ✅ NUM_PIN 수신 시 자동 상태조회 전송
            tcpService.OnNumPinReceived = async (pin) =>
            {
                //Debug.WriteLine($"[MainWindow] NUM_PIN 수신됨 → 상태 조회 전송 시작");
                await tcpService.SendProtocolAsync(ProtocolName.StatusCheck, pin: pin);
            };

            // ✅ STATE_ACTIVE=0 수신 시, 타이머 중단 및 UI 갱신
            tcpService.OnForceStopRequested = () =>
            {
                Dispatcher.Invoke(() =>
                {
                    wavTimer?.Stop();
                    micTimer?.Stop();
                    Debug.WriteLine("💡 STATE=false 수신 → 타이머 중단");

                    ellipse_status.Fill = Brushes.Gray;
                    MessageBox.Show("서버로부터 전송 중단 신호를 받았습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            };

            // ✅ STATE_ACTIVE=1 수신 시, 타이머 중단 및 UI 갱신
            tcpService.OnForceStartRequested = () =>
            {
                Dispatcher.Invoke(() =>
                {
                    wavTimer?.Start();
                    micTimer?.Start();
                    Debug.WriteLine("💡 STATE=true 수신 → 타이머 시작");

                    ellipse_status.Fill = Brushes.LimeGreen;
                    MessageBox.Show("서버로부터 전송 시작 신호를 받았습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            };

            // 서버 연결 시도
            bool success = await tcpService.ConnectAsync(ip, port, iPAddress.MAC, tcpService.GetText_connectStatus());

            if (success)
            {
                // 서버 연결 성공 시 설정 저장
                iPAddress.IP = ip;
                iPAddress.PORT = port;
                save_serveraddr(path_server, ip, port);
                ellipse_status.Fill = Brushes.LimeGreen; // 연결 상태 표시
            }
            else
            {
                MessageBox.Show("연결 실패", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                ellipse_status.Fill = Brushes.Red; // 연결 실패 시 상태 표시 (빨간색)
            }
        }

        private void UpdateMicLevelUI(float[] samples)
        {
            double sumSquares = 0;
            foreach (var s in samples)
                sumSquares += s * s;

            double rms = Math.Sqrt(sumSquares / samples.Length);
            double decibel = 20 * Math.Log10(rms + 1e-6);
            int level = (int)(rms * 100);

            Application.Current.Dispatcher.Invoke(() =>
            {
                micLevelBar.Value = level;
                decibelText.Text = $"{decibel:F1} dB";
            });
        }


        private void StartWavTimer()
        {
            if (wavTimer != null)
            {
                wavTimer.Stop();
                wavTimer.Dispose();
                wavTimer = null;
            }

            wavTimer = new System.Timers.Timer(AUDIO_SEND_PERIOD_SEC * 1000);
            wavTimer.Elapsed += async (s, e) =>
            {
                if (Directory.Exists(wav_file_path))
                {
                    // 🔍 하위 폴더까지 모든 .wav 파일 가져오기
                    var files = Directory.GetFiles(wav_file_path, "*.wav", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        var randomFile = files[new Random().Next(files.Length)];
                        byte[] wavData = File.ReadAllBytes(randomFile);

                        float[] samples = ReadWavFileSamples(randomFile);
                        UpdateMicLevelUI(samples);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            DrawWaveform(samples);
                            label_FileName.Text = System.IO.Path.GetFileName(randomFile);
                        });

                        await tcpService.SendProtocolAsync(
                            ProtocolName.AudioSend,
                            pin: tcpService.NumPin,
                            size: wavData.Length,
                            time: AUDIO_SEND_PERIOD_SEC,
                            source: "WAV",
                            fileData: wavData
                        );

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            label_FileName.Text = System.IO.Path.GetFileName(randomFile);
                        });

                        Debug.WriteLine($"[WAV 전송 완료] 파일: {System.IO.Path.GetFileName(randomFile)}, 크기: {wavData.Length} 바이트");
                    }
                    else
                    {
                        Debug.WriteLine("[WAV 전송 실패] 해당 폴더에 .wav 파일이 없습니다.");
                    }
                }
                else
                {
                    Debug.WriteLine("[WAV 전송 실패] 선택한 폴더 경로가 존재하지 않습니다.");
                }
            };

            //wavTimer.Start();
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

        private string wav_file_path; // 폴더 경로 저장 변수

        private void btn_mac_connect_Click(object sender, RoutedEventArgs e)
        {
            //if (tcpService == null || !tcpService.IsConnected)
            //{
            //    MessageBox.Show("서버에 연결되어 있지 않습니다. 먼저 서버에 연결해주세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            //    return;
            //}

            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                wav_file_path = dialog.SelectedPath;
                Debug.WriteLine($"[선택된 폴더] {wav_file_path}");

                StartWavTimer(); // 🔁 선택한 폴더를 기반으로 타이머 시작
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

            // 중심선 추가
            System.Windows.Shapes.Line midLine = new System.Windows.Shapes.Line
            {
                X1 = 0,
                Y1 = height / 2,
                X2 = width,
                Y2 = height / 2,
                Stroke = Brushes.Gray,
                StrokeThickness = 0.5
            };
            canvas_waveform.Children.Add(midLine);


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


        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void TextBox_TextChanged_1(object sender, TextChangedEventArgs e)
        {

        }

        private void radio_mic_Checked(object sender, RoutedEventArgs e)
        {
            wavTimer?.Stop();
            btn_browse.IsEnabled = false;
            ComboBox_mic.IsEnabled = true; // 콤보박스 활성화

            ComboBox_mic.Items.Clear();

            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var info = WaveIn.GetCapabilities(i);
                ComboBox_mic.Items.Add($"Device {i}: {info.ProductName}");
            }

            //Debug.WriteLine("🎤 마이크 장치 리스트 로딩 완료");
        }

        private void ComboBox_mic_DropDownClosed(object sender, EventArgs e)
        {
            if (ComboBox_mic.SelectedIndex < 0)
                return;

            int selectedDeviceIndex = ComboBox_mic.SelectedIndex;
            Debug.WriteLine($"✅ DropDownClosed: Index = {selectedDeviceIndex}");

            StartMicCapture(selectedDeviceIndex);
        }


        private void radio_csv_Checked(object sender, RoutedEventArgs e)
        {
            btn_browse.IsEnabled = true;
            ComboBox_mic.IsEnabled = false;

            StopMicCapture(); Thread.Sleep(100);

            ComboBox_mic.Items.Clear(); // 콤보박스 초기화

            Application.Current.Dispatcher.Invoke(() =>
            {
                micLevelBar.Value = 0;
                decibelText.Text = "0 dB";
            });
        }

        private void SetConnectionStatus(bool isConnected)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ellipse_status.Fill = isConnected ? Brushes.LimeGreen : Brushes.Gray;

            });
        }

        private void StartMicCapture(int deviceIndex)
        {
            // ✅ [기존 waveIn 중복 방지 및 자원 해제]
            if (waveIn != null)
            {
                waveIn.StopRecording();
                waveIn.Dispose();
                waveIn = null; // ⬅️ 안전하게 초기화도 해주세요
            }

            audioBuffer = new MemoryStream();

            waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = new WaveFormat(16000, 1),
                BufferMilliseconds = 100
            };
            waveIn.StartRecording();

            waveIn.DataAvailable += (s, a) =>
            {
                //Debug.WriteLine($"📢 [마이크 수신] BytesRecorded = {a.BytesRecorded}");

                // 캡처된 소리 데이터를 버퍼에 저장
                audioBuffer.Write(a.Buffer, 0, a.BytesRecorded);

                // ✅ 실시간 입력 레벨 계산 및 표시
                double sumSquares = 0;
                int sampleCount = a.BytesRecorded / 2;

                for (int i = 0; i < sampleCount; i++)
                {
                    short sample = BitConverter.ToInt16(a.Buffer, i * 2);
                    double normalized = sample / 32768.0;
                    sumSquares += normalized * normalized;
                }

                double rms = Math.Sqrt(sumSquares / sampleCount);   // 평균 제곱근 (0 ~ 1)
                double decibel = 20 * Math.Log10(rms + 1e-6);        // dB 단위 (로그 스케일)
                int level = (int)(rms * 100);                        // 0 ~ 100 범위로 변환

                Application.Current.Dispatcher.Invoke(() =>
                {
                    micLevelBar.Value = level;
                    decibelText.Text = $"{decibel:F1} dB";
                });

            };

            //waveIn.StartRecording();

            micTimer = new System.Timers.Timer(2000); // 2초 타이머
            micTimer.Elapsed += async (s, e) =>
            {
                micTimer.Stop();

                byte[] audioBytes = audioBuffer.ToArray();
                audioBuffer.SetLength(0); // 버퍼 초기화

                // 패킷 생성 및 전송
                //await SendPacketAsync(audioBytes, "MIC", 2);

                micTimer.Start();

                // 서버 전송 (추후 확장성)
                //await tcpService.SendProtocolAsync(
                //    ProtocolName.AudioSend,
                //    pin: tcpService.NumPin,
                //    size: audioBytes.Length,
                //    time: 2,
                //    source: "MIC",
                //    fileData: audioBytes
                //);
                //micTimer.Start();
            };
            micTimer.Start();
        }

        private void StopMicCapture()
        {
            micTimer?.Stop();
            waveIn?.StopRecording();
            waveIn?.Dispose();
            //audioBuffer?.Dispose();
            audioBuffer?.SetLength(0);
        }

        private void btn_disconnec_Click(object sender, RoutedEventArgs e)
        {
            wavTimer?.Stop(); // WAV 전송 타이머 중지
            micTimer?.Stop(); // 마이크 타이머 중지 (예외 방지)

            //textbox_ForderName.Text = null;
            ellipse_status.Fill = Brushes.Gray; // 연결 상태 표시 (회색)

            tcpService?.Disconnect(); // TCP 연결 해제
        }

        private void textbox_ForderName_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void ComboBox_mic_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Debug.WriteLine($"✅ SelectionChanged: Index = {ComboBox_mic.SelectedIndex}");

            if (ComboBox_mic.SelectedIndex < 0)
                return;

            StartMicCapture(ComboBox_mic.SelectedIndex);
        }

        private void textbox_filename_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}