using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace client_supervisor
{
    public class IPAddress_Local
    {
        public string IP { get; set; }
        public int Port { get; set; }
    }

    // 통신 상태 변화를 알리는 이벤트 핸들러 델리게이트
    public delegate void ConnectionStatusChangedEventHandler(object sender, bool isConnected);
    // 메시지 수신을 알리는 이벤트 핸들러 델리게이트
    public delegate void MessageReceivedEventHandler(object sender, string message);
    // 오류 발생을 알리는 이벤트 핸들러 델리게이트
    public delegate void ErrorOccurredEventHandler(object sender, string errorMessage);

    public class TcpClientService : IDisposable
    {
        private Socket _clientSocket;
        private CancellationTokenSource _receiveCts;
        private const int BufferSize = 8192;

        // UI에 연결 상태 변경을 알릴 이벤트
        public event ConnectionStatusChangedEventHandler ConnectionStatusChanged;
        // UI에 메시지 수신을 알릴 이벤트
        public event MessageReceivedEventHandler MessageReceived;
        // UI에 오류 발생을 알릴 이벤트
        public event ErrorOccurredEventHandler ErrorOccurred;

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

        public TcpClientService()
        {
            // 생성자에서는 특별한 초기화 없이 이벤트만 정의
        }

        /// <summary>
        /// 서버에 비동기적으로 연결을 시도합니다.
        /// </summary>
        /// <param name="ipAddress">서버 IP 주소</param>
        /// <param name="port">서버 포트</param>
        /// <returns>연결 성공 여부</returns>
        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            if (IsConnected)
            {
                Console.WriteLine("이미 서버에 연결되어 있습니다.");
                return true;
            }

            try
            {
                _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);

                Console.WriteLine($"서버에 연결 중... ({ipAddress}:{port})");
                await _clientSocket.ConnectAsync(serverEndPoint);

                Console.WriteLine("서버에 성공적으로 연결되었습니다!");
                OnConnectionStatusChanged(true);

                // 데이터 수신 시작
                _receiveCts = new CancellationTokenSource();
                _ = Task.Run(() => ReceiveDataAsync(_receiveCts.Token)); // 백그라운드에서 수신 루프 시작
                return true;
            }
            catch (FormatException)
            {
                OnErrorOccurred("오류: 올바른 IP 주소와 포트 번호를 입력하세요.");
            }
            catch (SocketException ex)
            {
                OnErrorOccurred($"연결 오류: {ex.Message} (코드: {ex.ErrorCode})");
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"예기치 않은 오류 발생: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// 서버로부터 데이터를 비동기적으로 수신하는 루프입니다.
        /// </summary>
        private async Task ReceiveDataAsync(CancellationToken cancellationToken)
        {
            // byte[] buffer = new byte[BufferSize]; // 이 임시 버퍼 대신 클래스 멤버인 _receiveBuffer 사용
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    // Socket.ReceiveAsync는 실제로 데이터를 받은 바이트 수를 반환
                    int bytesRead = await _clientSocket.ReceiveAsync(new ArraySegment<byte>(_receiveBuffer, 0, _receiveBuffer.Length), SocketFlags.None);

                    if (bytesRead == 0) // 서버가 연결을 끊었을 경우
                    {
                        Console.WriteLine("서버에서 연결을 종료했습니다.");
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
                Console.WriteLine("수신 소켓이 닫혔습니다.");
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
                    Console.WriteLine("수신 루프 종료. 연결을 끊습니다.");
                    Disconnect();
                }
                _currentMessageBuffer.Dispose(); // MemoryStream 자원 해제
            }
        }

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
                        if (_currentMessageBuffer.Length >= 8)
                        {
                            byte[] headerBytes = new byte[8];
                            _currentMessageBuffer.Read(headerBytes, 0, 8); // 버퍼에서 8바이트 읽기

                            // 바이트 배열을 정수로 변환 (리틀 엔디안 가정)
                            _totalMessageSize = BitConverter.ToInt32(headerBytes, 0); // 전체 크기 (4바이트)
                            _jsonSize = BitConverter.ToInt32(headerBytes, 4);       // JSON 크기 (4바이트)

                            // 헤더 유효성 검사 (예: JSON 크기가 전체 크기보다 클 수 없음)
                            if (_jsonSize > _totalMessageSize || _totalMessageSize <= 8) // 최소 크기는 헤더 8바이트 + 최소 JSON/파일 데이터
                            {
                                OnErrorOccurred($"잘못된 헤더 수신: TotalSize={_totalMessageSize}, JsonSize={_jsonSize}. 연결을 끊습니다.");
                                Disconnect(); // 잘못된 헤더는 치명적인 오류로 간주하여 연결 끊기
                                return; // 루프 종료
                            }

                            Console.WriteLine($"헤더 수신: 전체 크기={_totalMessageSize}, JSON 크기={_jsonSize}");
                            _currentState = ReceiveState.WaitingForJsonData; // 다음 상태로 전환
                        }
                        else
                        {
                            return;
                        }
                        break;

                    case ReceiveState.WaitingForJsonData:
                        // JSON 데이터를 받을 수 있는 충분한 데이터가 있는지 확인
                        if (_currentMessageBuffer.Length - _currentMessageBuffer.Position >= _jsonSize)
                        {
                            byte[] jsonBytes = new byte[_jsonSize];
                            _currentMessageBuffer.Read(jsonBytes, 0, _jsonSize); // JSON 크기만큼 데이터 읽기

                            string jsonData = Encoding.UTF8.GetString(jsonBytes);
                            Console.WriteLine($"JSON 데이터 수신: {jsonData}");

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
                        int fileDataSize = _totalMessageSize - 8 - _jsonSize; // 전체 - 헤더 - JSON 크기
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

                            Console.WriteLine($"파일 데이터 수신: {fileDataSize} 바이트");
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
                _currentMessageBuffer.Write(remainingBytes, 0, remainingBytes.Length);
            }
            else
            {
                // 남은 데이터가 없으면 스트림을 완전히 비움
                _currentMessageBuffer.SetLength(0);
                _currentMessageBuffer.Position = 0; // 위치도 0으로 재설정
            }
        }

        /// <summary>
        /// 서버로 메시지를 비동기적으로 전송합니다.
        /// </summary>
        /// <param name="message">전송할 메시지</param>
        /// <returns>전송 성공 여부</returns>
        public async Task<bool> SendMessageAsync(string message)
        {
            if (!IsConnected)
            {
                OnErrorOccurred("오류: 서버에 연결되어 있지 않습니다.");
                return false;
            }
            if (string.IsNullOrEmpty(message))
            {
                OnErrorOccurred("오류: 보낼 메시지를 입력하세요.");
                return false;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                await _clientSocket.SendAsync(new ArraySegment<byte>(data), SocketFlags.None);
                Console.WriteLine($"[전송]: {message}");
                return true;
            }
            catch (SocketException ex)
            {
                OnErrorOccurred($"전송 오류: {ex.Message}");
                Disconnect(); // 전송 오류 시 연결 끊기
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

        /// <summary>
        /// 서버와의 연결을 끊고 자원을 해제합니다.
        /// </summary>
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
                    Console.WriteLine("서버와의 연결이 끊어졌습니다.");
                    OnConnectionStatusChanged(false);
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
}
