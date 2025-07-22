using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace client_supervisor
{
    public struct WorkItem
    {
        public string Protocol { get; set; }
        public JObject JsonData { get; set; }
        public byte[] BinaryData { get; set; }

        public WorkItem()
        {
            Protocol = string.Empty;
            BinaryData = new byte[0];
            JsonData = new JObject();
        }
    }
    public class IPAddress_Local
    {
        public string IP { get; set; }
        public int Port { get; set; }
    }

    // 통신 상태 변화를 알리는 이벤트 핸들러 델리게이트
    public delegate void ConnectionStatusChangedEventHandler(object sender, bool isConnected);
    // 오류 발생을 알리는 이벤트 핸들러 델리게이트
    public delegate void ErrorOccurredEventHandler(object sender, string errorMessage);

    public class DataReceivedEventArgs : EventArgs
    {
        public string Protocol { get; set; }
        public JObject JsonData { get; private set; }
        public byte[] BinaryData { get; private set; }

        /// <summary>
        /// JSON 데이터와 바이너리 데이터를 모두 포함하는 생성자
        /// </summary>
        /// <param name="jsonData">수신된 JSON 문자열 (없으면 null 또는 빈 문자열)</param>
        /// <param name="binaryData">수신된 바이너리 바이트 배열 (없으면 null 또는 빈 배열)</param>
        public DataReceivedEventArgs(string protocol, JObject jsonData, byte[] binaryData)
        {
            Protocol = protocol ?? string.Empty;
            JsonData = jsonData ?? new JObject(); // null이면 빈 문자열로 초기화
            BinaryData = binaryData ?? new byte[0]; // null이면 빈 배열로 초기화
        }
    }
    // JSON과 바이너리 데이터 수신을 알리는 통합 이벤트 핸들러 델리게이트
    public delegate void DataReceivedEventHandler(object sender, DataReceivedEventArgs e);

    public class TcpClientService : IDisposable
    {
        private Socket _clientSocket;
        private CancellationTokenSource _receiveCts;
        private const int BufferSize = 8192; // 소켓에서 한 번에 읽어올 청크 크기

        public event ConnectionStatusChangedEventHandler ConnectionStatusChanged;
        public event DataReceivedEventHandler DataReceived;
        public event ErrorOccurredEventHandler ErrorOccurred;

        private SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1); // 동시 송신을 1개로 제한
        private ConcurrentDictionary<string, TaskCompletionSource<DataReceivedEventArgs>> _pendingResponses = new ConcurrentDictionary<string, TaskCompletionSource<DataReceivedEventArgs>>();
        private DispatcherSynchronizationContext _mainSynchronizationContext; // UI 스레드 컨텍스트


        public bool IsConnected => _clientSocket != null && _clientSocket.Connected;

        private byte[] _receiveBuffer = new byte[BufferSize]; // 소켓에서 직접 읽어올 임시 버퍼 (청크 단위)
        private MemoryStream _currentMessageBuffer = new MemoryStream(); // 현재 메시지를 구성하는 데이터를 축적할 버퍼

        private enum ReceiveState
        {
            WaitingForHeader,       // 헤더(전체 크기, JSON 크기) 대기
            WaitingForJsonData,     // JSON 데이터 대기
            WaitingForFileData      // 파일 데이터 대기
        }

        private ReceiveState _currentState = ReceiveState.WaitingForHeader; // 현재 수신 상태
        private int _totalMessageSize = 0; // 헤더에서 읽은 전체 메시지 크기 (JSON + Binary 데이터의 크기, 헤더 자체는 포함하지 않음)
        private int _jsonSize = 0;         // 헤더에서 읽은 JSON 데이터 크기

        // 파싱된 데이터를 임시로 저장할 변수
        private JObject _parsedJsonConv = new JObject();
        private byte[] _parsedBinaryData = new byte[0];
        private string _parseProtocol = string.Empty;

        public TcpClientService()
        {
            _mainSynchronizationContext = (DispatcherSynchronizationContext)SynchronizationContext.Current;
            if (_mainSynchronizationContext == null)
            {
                // UI 스레드에서 인스턴스화되지 않았다면, 현재 스레드의 컨텍스트를 사용
                // 하지만 WPF 애플리케이션에서는 항상 UI 스레드에서 생성되어야 합니다.
                _mainSynchronizationContext = new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher);
            }
        }

        public async Task<DataReceivedEventArgs> SendMessageAndWaitForResponseAsync(WorkItem item, string responseProtocol, int timeoutMs = 5000)
        {
            if (!IsConnected)
            {
                OnErrorOccurred("오류: 서버에 연결되어 있지 않습니다. 메시지를 보낼 수 없습니다.");
                throw new InvalidOperationException("서버에 연결되어 있지 않습니다.");
            }

            // 송신 락 획득: SendMessageWithHeaderAsync 또는 다른 SendAndWait 요청과 겹치지 않도록 합니다.
            await _sendLock.WaitAsync();
            try
            {
                // 응답을 받을 TaskCompletionSource 생성
                var tcs = new TaskCompletionSource<DataReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                string responseKey = responseProtocol;

                // 이미 해당 응답 프로토콜에 대한 대기 중인 요청이 있다면 오류 발생
                if (!_pendingResponses.TryAdd(responseKey, tcs))
                {
                    throw new InvalidOperationException($"응답 키 '{responseKey}'에 대한 요청이 이미 대기 중입니다. 중복 요청 불가.");
                }

                // 메시지 구성 (SendMessageWithHeaderAsync와 동일한 로직)
                item.JsonData["PROTOCOL"] = item.Protocol; // WorkItem에 프로토콜이 있지만, JSON에 한 번 더 넣어줍니다.
                string json_conv = item.JsonData.ToString(Newtonsoft.Json.Formatting.None);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json_conv ?? "");
                byte[] fileBytes = item.BinaryData ?? new byte[0];

                int jsonSize = jsonBytes.Length;
                int totalPayloadSize = jsonSize + fileBytes.Length;

                byte[] totalSizeBytes = BitConverter.GetBytes(totalPayloadSize);
                byte[] jsonSizeBytes = BitConverter.GetBytes(jsonSize);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(totalSizeBytes);
                    Array.Reverse(jsonSizeBytes);
                }

                byte[] header = new byte[8];
                Buffer.BlockCopy(totalSizeBytes, 0, header, 0, 4);
                Buffer.BlockCopy(jsonSizeBytes, 0, header, 4, 4);

                using (MemoryStream stream = new MemoryStream())
                {
                    stream.Write(header, 0, header.Length);
                    stream.Write(jsonBytes, 0, jsonBytes.Length);
                    stream.Write(fileBytes, 0, fileBytes.Length);

                    byte[] fullMessage = stream.ToArray();
                    await _clientSocket.SendAsync(new ArraySegment<byte>(fullMessage), SocketFlags.None);
                    Console.WriteLine($"[SendAndWait] Protocol : {item.Protocol}, Total Size: {fullMessage.Length} bytes (Header: 8, JSON: {jsonSize}, Binary: {fileBytes.Length})");
                }

                // 타임아웃 작업 생성
                var timeoutTask = Task.Delay(timeoutMs);

                // 응답 Task 또는 타임아웃 Task 중 먼저 완료되는 것을 기다림
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // 타임아웃 발생: 대기 목록에서 제거하고 예외 발생
                    _pendingResponses.TryRemove(responseKey, out _);
                    tcs.TrySetCanceled(); // TCS를 Canceled 상태로 설정하여 대기 중인 Task를 취소
                    throw new TimeoutException($"서버 응답 시간 초과: '{responseProtocol}' 프로토콜에 대한 응답을 {timeoutMs}ms 내에 수신하지 못했습니다.");
                }

                // 정상적으로 응답이 왔으므로 결과를 반환
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                // SendAndWait 과정에서 발생한 모든 예외를 다시 던짐
                OnErrorOccurred($"SendMessageAndWaitForResponseAsync 오류: {ex.Message}");
                _sendLock.Release();
                throw;
            }
            finally
            {
            }
        }

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

                _receiveCts = new CancellationTokenSource();
                _ = Task.Run(() => ReceiveDataAsync(_receiveCts.Token));

                Console.WriteLine("서버에 성공적으로 연결되었습니다!");
                OnConnectionStatusChanged(true);
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

        private async Task ReceiveDataAsync(CancellationToken cancellationToken)
        {
             try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    // MemoryStream에 데이터를 추가하기 전에, 읽기 Position을 저장.
                    // 이 위치에서부터 새로운 데이터를 쓸 수 있도록 함.
                    long previousPosition = _currentMessageBuffer.Position;
                    _currentMessageBuffer.Position = _currentMessageBuffer.Length; // 현재 쓰기 가능한 맨 끝 위치로 이동

                    int bytesRead = await _clientSocket.ReceiveAsync(new ArraySegment<byte>(_receiveBuffer, 0, _receiveBuffer.Length), SocketFlags.None);

                    if (bytesRead == 0)
                    {
                        Console.WriteLine("서버에서 연결을 종료했습니다.");
                        break;
                    }

                    // 받은 데이터를 현재 메시지 버퍼에 추가. MemoryStream의 Length는 bytesRead만큼 늘어남.
                    _currentMessageBuffer.Write(_receiveBuffer, 0, bytesRead);

                    _currentMessageBuffer.Position = previousPosition; // 메시지 파싱 시작 위치로 되돌림

                    ProcessReceivedData(); // 여기서 수정된 로직을 사용합니다.
                }
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("수신 소켓이 닫혔습니다.");
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.ConnectionReset || ex.SocketErrorCode == SocketError.Interrupted)
                {
                    OnErrorOccurred("서버와의 연결이 강제로 끊어졌습니다.");
                    OnConnectionStatusChanged(false);
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
            }
        }

        private void ProcessReceivedData()
        {
            while (true) // 버퍼에 여러 메시지가 있을 수 있으므로 반복 처리
            {
                long availableBytes = _currentMessageBuffer.Length - _currentMessageBuffer.Position;

                switch (_currentState)
                {
                    case ReceiveState.WaitingForHeader:
                        if (availableBytes >= 8)
                        {
                            byte[] headerBytes = new byte[8];
                            _currentMessageBuffer.Read(headerBytes, 0, 8);

                            byte[] totalSizeBytes = new byte[4];
                            Buffer.BlockCopy(headerBytes, 0, totalSizeBytes, 0, 4);

                            byte[] jsonSizeBytes = new byte[4];
                            Buffer.BlockCopy(headerBytes, 4, jsonSizeBytes, 0, 4);

                            if (BitConverter.IsLittleEndian)
                            {
                                Array.Reverse(totalSizeBytes);
                                Array.Reverse(jsonSizeBytes);
                            }

                            _totalMessageSize = BitConverter.ToInt32(totalSizeBytes, 0);
                            _jsonSize = BitConverter.ToInt32(jsonSizeBytes, 0);

                            if (_jsonSize < 0 || _totalMessageSize < 0 || _jsonSize > _totalMessageSize)
                            {
                                OnErrorOccurred($"잘못된 헤더 수신: TotalSize={_totalMessageSize}, JsonSize={_jsonSize}. 연결을 끊습니다.");
                                Disconnect();
                                return;
                            }

                            //Console.WriteLine($"헤더 수신: 전체 크기={_totalMessageSize}, JSON 크기={_jsonSize}");
                            _currentState = ReceiveState.WaitingForJsonData;
                            continue;
                        }
                        else
                        {
                            return;
                        }

                    case ReceiveState.WaitingForJsonData:
                        if (availableBytes >= _jsonSize)
                        {
                            string jsonString = "";
                            if (_jsonSize > 0)
                            {
                                byte[] jsonBytes = new byte[_jsonSize];
                                _currentMessageBuffer.Read(jsonBytes, 0, _jsonSize);
                                jsonString = Encoding.UTF8.GetString(jsonBytes);
                                try
                                {
                                    _parsedJsonConv = JObject.Parse(jsonString);
                                    if (_parsedJsonConv.ContainsKey("PROTOCOL"))
                                    {
                                        _parseProtocol = _parsedJsonConv["PROTOCOL"].ToString();
                                    }
                                    else
                                    {
                                        _parseProtocol = "UNKNOWN_PROTOCOL";
                                    }
                                    //Console.WriteLine($"JSON 데이터 수신: {_parsedJsonConv.ToString(Newtonsoft.Json.Formatting.None)}");
                                }
                                catch (Exception ex)
                                {
                                    OnErrorOccurred($"JSON 파싱 오류: {ex.Message}. 수신된 JSON: {jsonString}. 연결을 끊습니다.");
                                    Disconnect();
                                    return;
                                }
                            }
                            else
                            {
                                _parsedJsonConv = new JObject();
                                _parseProtocol = "NO_JSON";
                            }

                            _currentState = ReceiveState.WaitingForFileData;
                            continue;
                        }
                        else
                        {
                            return;
                        }

                    case ReceiveState.WaitingForFileData:
                        int fileDataSize = _totalMessageSize - _jsonSize;

                        if (fileDataSize < 0)
                        {
                            OnErrorOccurred($"파일 데이터 크기 계산 오류: FileDataSize={fileDataSize}. 연결을 끊습니다.");
                            Disconnect();
                            return;
                        }

                        if (availableBytes >= fileDataSize)
                        {
                            if (fileDataSize > 0)
                            {
                                byte[] fileDataBytes = new byte[fileDataSize];
                                _currentMessageBuffer.Read(fileDataBytes, 0, fileDataSize);
                                _parsedBinaryData = fileDataBytes;
                                //Console.WriteLine($"파일 데이터 수신: {fileDataSize} 바이트");
                            }
                            else
                            {
                                _parsedBinaryData = new byte[0];
                                //Console.WriteLine("바이너리 데이터 없음.");
                            }

                            DataReceivedEventArgs receivedData = new DataReceivedEventArgs(_parseProtocol, _parsedJsonConv, _parsedBinaryData);

                            // 대기 중인 요청에 대한 응답인지 확인
                            if (_pendingResponses.TryRemove(receivedData.Protocol, out var tcs))
                            {
                                // 대기 중인 요청을 완료시킴
                                _mainSynchronizationContext.Post(_ => tcs.TrySetResult(receivedData), null);
                                _sendLock.Release(); // 응답이 왔으므로 송신 락 해제
                                Console.WriteLine($"[SendAndWait] 응답 수신 및 락 해제: {receivedData.Protocol}");
                            }
                            else
                            {
                                // 일반 메시지인 경우, DataReceived 이벤트를 발생시킴
                                OnDataReceived(receivedData);
                                Console.WriteLine($"[Receive] 일반 메시지 수신: {receivedData.Protocol}");
                            }

                            CleanupMessageBuffer();

                            _currentState = ReceiveState.WaitingForHeader;
                            _totalMessageSize = 0;
                            _jsonSize = 0;

                            if (_currentMessageBuffer.Length > 0)
                            {
                                continue;
                            }
                            else
                            {
                                return;
                            }
                        }
                        else
                        {
                            return;
                        }

                    default:
                        OnErrorOccurred($"알 수 없는 수신 상태: {_currentState}. 연결을 끊습니다.");
                        Disconnect();
                        return;
                }
            } // end while (true)
        }


        /// <summary>
        /// _currentMessageBuffer의 현재 Position부터 끝까지 남은 데이터를 스트림 시작 부분으로 이동시킵니다.
        /// 이 메서드 호출 후 _currentMessageBuffer.Position은 0이 됩니다.
        /// </summary>
        private void CleanupMessageBuffer()
        {
            long currentPosition = _currentMessageBuffer.Position;
            long remainingLength = _currentMessageBuffer.Length - currentPosition;

            if (remainingLength > 0)
            {
                byte[] remainingBytes = new byte[remainingLength];
                _currentMessageBuffer.Read(remainingBytes, 0, (int)remainingLength);

                _currentMessageBuffer.SetLength(0);
                _currentMessageBuffer.Position = 0;
                _currentMessageBuffer.Write(remainingBytes, 0, remainingBytes.Length);
                _currentMessageBuffer.Position = 0;
            }
            else
            {
                _currentMessageBuffer.SetLength(0);
                _currentMessageBuffer.Position = 0;
            }
        }

        // (SendMessageWithHeaderAsync, Disconnect, OnConnectionStatusChanged, OnDataReceived, OnErrorOccurred, Dispose methods remain the same)
        public async Task<bool> SendMessageWithHeaderAsync(WorkItem item)
        {
            if (!IsConnected)
            {
                OnErrorOccurred("오류: 서버에 연결되어 있지 않습니다.");
                return false;
            }

            // --- SendAndWait와 겹치지 않도록 SendLock 추가 ---
            // 응답을 기다리지 않는 메시지라도 동시 송신을 막기 위해 락을 사용합니다.
            // 이렇게 하면 TCP 소켓을 통한 메시지 순서가 보장됩니다.
            await _sendLock.WaitAsync();
            try
            {
                item.JsonData["PROTOCOL"] = item.Protocol;

                string json_conv = item.JsonData.ToString(Newtonsoft.Json.Formatting.None);

                byte[] jsonBytes = Encoding.UTF8.GetBytes(json_conv ?? "");
                byte[] fileBytes = item.BinaryData ?? new byte[0];

                int jsonSize = jsonBytes.Length;
                int totalPayloadSize = jsonSize + fileBytes.Length;

                byte[] totalSizeBytes = BitConverter.GetBytes(totalPayloadSize);
                byte[] jsonSizeBytes = BitConverter.GetBytes(jsonSize);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(totalSizeBytes);
                    Array.Reverse(jsonSizeBytes);
                }

                byte[] header = new byte[8];
                Buffer.BlockCopy(totalSizeBytes, 0, header, 0, 4);
                Buffer.BlockCopy(jsonSizeBytes, 0, header, 4, 4);

                using (MemoryStream stream = new MemoryStream())
                {
                    stream.Write(header, 0, header.Length);
                    stream.Write(jsonBytes, 0, jsonBytes.Length);
                    stream.Write(fileBytes, 0, fileBytes.Length);

                    byte[] fullMessage = stream.ToArray();
                    await _clientSocket.SendAsync(new ArraySegment<byte>(fullMessage), SocketFlags.None);
                    Console.WriteLine($"[Send] Protocol : {item.Protocol}, Total Size: {fullMessage.Length} bytes (Header: 8, JSON: {jsonSize}, Binary: {fileBytes.Length})");
                }
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
            finally
            {
                _sendLock.Release();
            }
            return false;
        }

        public void Disconnect()
        {
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

            _currentMessageBuffer?.Dispose();
            _currentMessageBuffer = new MemoryStream();
            _currentState = ReceiveState.WaitingForHeader;
            _totalMessageSize = 0;
            _jsonSize = 0;

            foreach (var tcs in _pendingResponses.Values)
            {
                tcs.TrySetException(new OperationCanceledException("연결이 끊어져 요청이 취소되었습니다."));
            }
            _pendingResponses.Clear();
            // 모든 락을 강제로 해제하여 다음 연결 시 락이 걸려있지 않도록 합니다.
            while (_sendLock.CurrentCount < 1)
            {
                _sendLock.Release();
            }
        }

        protected virtual void OnConnectionStatusChanged(bool isConnected)
        {
            ConnectionStatusChanged?.Invoke(this, isConnected);
        }

        protected virtual void OnDataReceived(DataReceivedEventArgs e)
        {
            DataReceived?.Invoke(this, e);
        }

        protected virtual void OnErrorOccurred(string errorMessage)
        {
            ErrorOccurred?.Invoke(this, errorMessage);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disconnect();
                _currentMessageBuffer?.Dispose();
                _currentMessageBuffer = null;
            }
        }

        ~TcpClientService()
        {
            Dispose(false);
        }
    }
}
