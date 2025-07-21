using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace client_supervisor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string path_server = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_address.json");
        string path_maps = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps");
        string path_map_meta = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps/meta_maps.json");

        private DispatcherTimer? timer_curr;
        private TcpClientService clientService;
        private IPAddress_Local iPAddress;
        private TaskCompletionSource<DataReceivedEventArgs> _responseTcs;
        private readonly object _responseTcsLock = new object(); // _responseTcs 접근을 위한 락 객체

        /*-------------------------------------------------------------------*/
        // 각종 변수 선언
        // 지도 출력 관련
        private bool isPinModeEnabled { get; set; }
        private double _minScale = 1.0;

        // 이상상황 목록
        public ObservableCollection<AnomalyLog> List_Daily_Anomaly { get; set; } = new ObservableCollection<AnomalyLog>();

        // 지도 목록
        public ObservableCollection<MapSector> MapSectors { get; set; } = new ObservableCollection<MapSector>();
        private int idx_map = 0;
        List<MapSector> Map_Add = new();
        List<int> Map_Removed = new();

        // 핀 목록
        public ObservableCollection<ClientPin> PinList { get; set; } = new ObservableCollection<ClientPin>();
        public List<ClientPin> PinList_Modified { get; set; } = new List<ClientPin>();

        // 등록되지 않은 MAC 주소 목록
        public ObservableCollection<string> MACList { get; set; } = new ObservableCollection<string>();

        // 고장 원인 종류
        public Dictionary<int ,string> List_Kind_Error { get; set; } = new Dictionary<int, string>();

        // 이상 여부 종류
        public Dictionary<int, string> List_Kind_Anomaly { get; set; } = new Dictionary<int, string>();

        public MainWindow()
        {
            this.AddRecvProtocol();
            InitializeComponent();
            InitializeTimer();
            this.isPinModeEnabled = false;      // 핀 추가 값 초기화

            this.load_serveraddr(this.path_server);
        }
        /*-------------------------------------------------------------------*/
        // 로컬 json 파일 제어
        // maps 폴더에 저장된 메타데이터 불러오기
        private ObservableCollection<MapSector> load_maplist(bool showimage = false)
        {
            baseImage.Source = null;

            // maps 폴더가 루트에 존재하는지 확인
            string path_map = "maps";
            if (!Directory.Exists(path_map))
            {
                Directory.CreateDirectory(path_map);
            }

            // map의 메타파일이 존재하는지 확인
            if (!File.Exists(this.path_map_meta))
            {
                File.WriteAllText(this.path_map_meta, "[]");     // 기본 객체 생성 후 파일 저장
            }

            string read_string = File.ReadAllText(this.path_map_meta);
            ObservableCollection<MapSector> mapSectors = new ObservableCollection<MapSector>();
            mapSectors = JsonConvert.DeserializeObject<ObservableCollection<MapSector>>(read_string);     // 문자열을 json으로 변환

            Window_Manage_Map manage_Map = new Window_Manage_Map(mapSectors);       // 화면 출력을 하지 않음
            manage_Map.conn_event();        // 파일에서 불러온 목록들에 대하여 이벤트 연결

            // 이미지 목록을 불러왔으면, 이미지를 출력
            if (showimage && mapSectors.Count > 0)
            {
                LoadImageSafely(System.IO.Path.Combine(path_maps, mapSectors.First().Path));
                this.idx_map = 0;
                this.FitToViewer();
            }

            return mapSectors;
        }
        // 서버 주소 파일 불러오기
        private void load_serveraddr(string path_meta)
        {
            // 서버 주소파일이 존재하는지 확인
            if (!File.Exists(path_meta))
            {
                File.WriteAllText(path_meta, "{\"IP\" : \"\", \"PORT\" : 0}");     // 기본 객체 생성 후 파일 저장
            }

            // 파일 읽기 
            string read_string = File.ReadAllText(path_meta);

            var read_json = JsonConvert.DeserializeObject<IPAddress_Local>(read_string);     // 문자열을 json으로 변환
            this.iPAddress = new IPAddress_Local();
            this.iPAddress.IP = read_json.IP;
            this.iPAddress.Port = read_json.Port;
        }
        // 서버 주소 파일 저장하기
        private void save_serveraddr(string path_meta, string IP, int port)
        {
            Dictionary<string, object> dict_json = new Dictionary<string, object>();

            dict_json.Add("IP", IP);
            dict_json.Add("PORT", port);

            string json = JsonConvert.SerializeObject(dict_json);
            File.WriteAllText(this.path_server, json);
        }
        // 로컬 도면을 메타데이터에 저장
        private void ClickManageMap(object sender, RoutedEventArgs e)
        {
            Window_Manage_Map window_Manage_Map = new Window_Manage_Map(this.MapSectors);
            if (window_Manage_Map.ShowDialog() == true)
            {
                // 캡쳐된 MapSectors를 원본 MapSectors로 덮어쓰기
                ObservableCollection<MapSector> MapSectors_recv = window_Manage_Map.MapSectors;

                Console.WriteLine($"변경된 MapSectors : {this.MapSectors.Count}");

                // 1. 삭제된 파일 처리
                Console.WriteLine($"\n1. 삭제된 파일 처리 시도:");
                this.Map_Removed = this.compare_maplist_delete(this.MapSectors, MapSectors_recv);

                // 2. 새로 추가된 파일 처리
                Console.WriteLine($"\n2. 새로 추가된 파일 처리 시도:");
                this.Map_Add = this.compare_maplist_add(MapSectors_recv, this.MapSectors);

                // 3. 메타 JSON 파일 업데이트 (순서 변경 및 속성 업데이트 포함)
                this.MapSectors = MapSectors_recv;
                this.save_maplist(true);
            }

            this.MapSectors = this.load_maplist(true);      // 도면 목록 다시 불러오기
        }
        // 서버로부터 받은 지도 목록 저장
        public void save_maplist(bool save_path = false)
        {
            List<object> list_json = new List<object>();

            foreach (MapSector mapData in this.MapSectors)
            {
                Dictionary<string, object> dict_meta = new Dictionary<string, object>();
                dict_meta.Add("NUM_MAP", mapData.Num_Map);
                dict_meta.Add("IDX", mapData.Idx);
                dict_meta.Add("NAME", mapData.Name);
                dict_meta.Add("PATH", mapData.Path); // 업데이트된 Path를 사용

                string fullPathForSize = "";
                if (save_path) fullPathForSize = System.IO.Path.Combine(this.path_maps, mapData.Path);

                try
                {
                    long file_size = File.Exists(fullPathForSize) ? new FileInfo(fullPathForSize).Length : 0;
                    dict_meta.Add("SIZE", file_size);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  -> 파일 크기 가져오기 실패: {fullPathForSize} - {ex.Message}");
                    dict_meta.Add("SIZE", 0); // 실패 시 0 또는 기본값 설정
                }

                list_json.Add(dict_meta);
            }

            try
            {
                string json = JsonConvert.SerializeObject(list_json, Formatting.Indented); // 가독성을 위해 Indented 옵션 추가
                File.WriteAllText(this.path_map_meta, json);
                Console.WriteLine($"  -> 메타 JSON 파일 저장 성공: {this.path_map_meta}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  -> 메타 JSON 파일 저장 실패: {this.path_map_meta} - {ex.Message}");
            }
        }
        // 지도 목록 비교 - 실행 후 삭제
        public List<int> compare_maplist_delete(ObservableCollection<MapSector> src, ObservableCollection<MapSector> tgt, bool filedelete = true)
        {
            List<int> list_remove = new();
            baseImage.Source = null;
            System.Threading.Thread.Sleep(50);

            foreach (var item in src)
            {
                Console.WriteLine($"  현재 항목 : {item.Name} ({item.Path})");
                // 캡쳐 기준 해당 항목이 없는 경우 삭제
                if (!tgt.Contains(item))
                {
                    list_remove.Add(item.Num_Map);      // 삭제할 지도 번호를 목록에 추가

                    if (filedelete)
                    {
                        string fullPathToDelete = System.IO.Path.Combine(this.path_maps, item.Path);
                        File.Delete(fullPathToDelete);

                        Console.WriteLine($"  삭제 변동사항 발생, 파일 삭제 시도 : {item.Name} ({fullPathToDelete})");
                    }
                }
            }

            return list_remove;
        }
        // 지도 목록 비교 - 실행 후 추가
        public List<MapSector> compare_maplist_add(ObservableCollection<MapSector> src, ObservableCollection<MapSector> tgt, bool filecopy = true)
        {
            List<MapSector> list_add = new();

            foreach (var item in src)
            {
                Console.WriteLine($"  현재 항목 : {item.Name} ({item.Path})");
                // 캡쳐 기준 해당 항목이 없는 경우 또는 캡쳐 기준 갯수가 하나도 없는 경우 추가
                if (!tgt.Contains(item))
                {
                    list_add.Add(item.Copy());          // 추가된 사항에 대해서 리스트에 추가
                    if (filecopy)
                    {
                        string targetFilePath = System.IO.Path.Combine(this.path_maps, System.IO.Path.GetFileName(item.Path_Origin));
                        if (File.Exists(item.Path_Origin))
                        {
                            Console.WriteLine($"원본 경로 : {item.Path_Origin}");
                            File.Copy(item.Path_Origin, targetFilePath, true);

                        }
                        Console.WriteLine($"  추가 변동사항 발생, 항목 추가 및 파일 복사 시도 : {item.Name} ({targetFilePath})");
                    }
                }
            }
            return list_add;
        }

        /*-------------------------------------------------------------------*/
        // 시간 확인 타이머 설정
        private void InitializeTimer()          // 타이머 초기화하는 메서드 
        {
            label_dt_curr.Content = DateTime.Now.ToString("f");
            this.timer_curr = new DispatcherTimer();
            // 타이머 간격 설정: 1초 (Timespan.FromSeconds(1))
            this.timer_curr.Interval = TimeSpan.FromSeconds(1);
            // 타이머의 Tick 이벤트 핸들러 연결
            this.timer_curr.Tick += Timer_Tick;
            // 타이머 시작
            this.timer_curr.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            // 현재 시간을 가져와서 "HH:mm:ss" 형식으로 라벨의 Content를 업데이트
            label_dt_curr.Content = DateTime.Now.ToString("f");
        }

        /*-------------------------------------------------------------------*/
        // 서버와 연결 버튼 클릭 시 실행
        private void ConnTCP_Click(object sender, RoutedEventArgs e)
        {
            // 소켓 생성 및 초기화
            this.InitializeCustomComponents();

            // 서버와 연결 시도
            this.clientService.ConnectAsync(this.iPAddress.IP, this.iPAddress.Port);
        }

        private void DisconnTCP_Click(object sender, RoutedEventArgs e)
        {
            this.clientService.Dispose();
            baseImage.Source = null;        // 도면 이미지 초기화

            // 각종 리스트 초기화
            this.List_Kind_Anomaly.Clear();
            this.List_Kind_Error.Clear();
            this.PinList.Clear();
            this.MapSectors.Clear();        // 도면 초기화
        }
        private void EditTCP_Click(object sender, RoutedEventArgs e)
        {
            this.load_serveraddr(this.path_server);
            Window_Setup_Server setup_server = new Window_Setup_Server(this.iPAddress.IP, this.iPAddress.Port);
            if (setup_server.ShowDialog() == true)
            {
                Int32.TryParse(setup_server.TextBox_PORT.Text, out int port);
                this.save_serveraddr(this.path_server, setup_server.TextBox_IP.Text, port);
                MessageBox.Show("서버 주소가 변경되었습니다.", "서버 주소 변경 성공");
            }
        }

        /*-------------------------------------------------------------------*/
        // 소켓 부분 이벤트 핸들러
        private void InitializeCustomComponents()
        {
            // TcpClientService 인스턴스 생성
            clientService = new TcpClientService();

            // TcpClientService의 이벤트 핸들러 등록
            clientService.ConnectionStatusChanged += ClientService_ConnectionStatusChanged;
            clientService.ErrorOccurred += ClientService_ErrorOccurred;
            clientService.DataReceived += ClientService_DataReceived;
        }
        
        // --- TcpClientService 이벤트 핸들러 ---
        private void ClientService_ConnectionStatusChanged(object sender, bool isConnected)
        {
            // UI 스레드에서 UI를 업데이트합니다.
            Dispatcher.Invoke(() =>
            {
                if (isConnected)
                {
                    OnConnectedToServer();
                }
                else
                {
                    OnDisconnectedFromServer();
                }
                UpdateStatus(isConnected ? "연결됨" : "연결 끊김");
            });
        }
        // 서버에 연결되었을 때 실행되는 메서드
        private async Task OnConnectedToServer()
        {
            Header_Manage_Map.IsEnabled = true;
            Header_Manage_Pin.IsEnabled = true;

            Header_Conn.IsEnabled = false;
            Header_Disconn.IsEnabled = true;
            WorkItem send_item = new();

            // 접속 요청 송신 및 수신
            send_item.Protocol = "1-0-0";
            await this.ExcuteCommand_Send(send_item);

            // 연결이 유지될때만 아래 태스크 실행, 연결이 종료되면 수행 불가 
            if (this.clientService.IsConnected)
            {
                // 고장원인 목록 요청
                send_item.Protocol = "1-0-1";
                await this.ExcuteCommand_Send(send_item);

                // 이상 상태 처리 목록 요청
                send_item.Protocol = "1-0-2";
                await this.ExcuteCommand_Send(send_item);

                // 지도 목록 요청
                send_item.Protocol = "1-3-0";
                await this.ExcuteCommand_Send(send_item);

                // 추가해야하는 지도가 있다면 서버에 요청
                for (int i = 0; i < Map_Add.Count; i++)
                {
                    send_item.Protocol = "1-3-3";
                    send_item.JsonData["NUM_MAP"] = Map_Add[i].Num_Map;
                    this.ExcuteCommand_Send(send_item);     // 데이터 요청 송신
                }

                // JSON 객체 초기화
                send_item.JsonData = new JObject();

                // 저장된 메타데이터 불러오기
                //this.MapSectors = this.load_maplist(true);

                // 핀 목록 요청
                send_item.Protocol = "1-1-0";
                await this.ExcuteCommand_Send(send_item);
            }
        }
        // 서버 연결이 끊어졌을 때 실행되는 메서드
        private void OnDisconnectedFromServer()
        {
            Header_Manage_Map.IsEnabled = false;
            Header_Manage_Pin.IsEnabled = false;

            this.DeleteRadioButtonToPanel(wrap_kind_anomaly);

            Header_Conn.IsEnabled = true;
            Header_Disconn.IsEnabled = false;

            // 현재 대기 중인 TaskCompletionSource가 있다면 취소
            lock (_responseTcsLock)
            {
                _responseTcs?.TrySetCanceled();
                _responseTcs = null;
            }
        }
        // 특정 응답을 기다리는 비동기 헬퍼 메서드
        private Task<DataReceivedEventArgs> WaitForResponseAsync()
        {
            lock (_responseTcsLock)
            {
                // 이미 대기 중인 TaskCompletionSource가 있다면 오류 또는 취소 처리
                if (_responseTcs != null && !_responseTcs.Task.IsCompleted)
                {
                    _responseTcs.TrySetCanceled(); // 이전 대기 취소
                }
                _responseTcs = new TaskCompletionSource<DataReceivedEventArgs>();
                return _responseTcs.Task;
            }
        }

        // 통합 데이터 수신 이벤트 핸들러
        private void ClientService_DataReceived(object sender, DataReceivedEventArgs e)
        {
            // UI 업데이트는 UI 스레드에서 수행되어야 합니다.
            Dispatcher.Invoke(new Action(() =>
            {
                // 데이터 수신 시 프로토콜 인스턴스 실행
                WorkItem item = new WorkItem {Protocol = e.Protocol, JsonData = e.JsonData, BinaryData = e.BinaryData };
                this.ExcuteCommand_Recv(item);

                _responseTcs.TrySetResult(e);
                _responseTcs = null;
            }));
        }

        private void ClientService_ErrorOccurred(object sender, string errorMessage)
        {
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine(errorMessage);
            });
        }

        /*-------------------------------------------------------------------*/
        // UI 업데이트 부분
        private void UpdateStatus(string status)
        {
            label_socket.Content = status;
        }

        // 세부사항 및 상황 처리사항 부분 활성화, 비활성화 메서드
        public void activateDetailPanel(bool flag = true)
        {
            // 라디오버튼 활성화
            foreach (RadioButton radioButton in wrap_kind_anomaly.Children)
            {
                radioButton.IsEnabled = flag;
            }

            // 텍스트 입력부분 비활성화
            textbox_proc_manager.IsEnabled = flag;
            textbox_proc_memo.IsEnabled = flag;
        }

        // 라디오 버튼 동적 생성
        private void AddRadioButtonToPanel(WrapPanel parentPanel, string content, string groupName, bool isChecked = false)
        {
            RadioButton newRadioButton = new RadioButton
            {
                Content = content, // 라디오 버튼에 표시될 텍스트
                GroupName = groupName, // 그룹 이름 설정
                IsChecked = isChecked, // 초기 선택 상태 설정
                Margin = new Thickness(3), // 여백 설정
                FontSize = 12, // 글자 크기 설정
            };

            // 이벤트 핸들러 추가
            newRadioButton.Checked += RadioButton_Checked;
            newRadioButton.IsEnabled = false;
            parentPanel.Children.Add(newRadioButton); // 패널에 라디오 버튼 추가
        }
        // 생성된 라디오버튼 삭제
        private void DeleteRadioButtonToPanel(WrapPanel parentPanel)
        {
            if (parentPanel != null)
            {
                parentPanel.Children.Clear();
            }
        }
        // 라디오 버튼 Checked 이벤트 핸들러
        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            RadioButton? radioButton = sender as RadioButton;
            if (radioButton != null)
            {
                //MessageBox.Show($"{radioButton.Content}이(가) 선택되었습니다.");
            }
        }
        // 라디오 버튼 생성
        private void CreateRadioButtons_Click(Dictionary<int, string> dict_name)
        {
            wrap_kind_anomaly.Children.Clear();

            foreach (KeyValuePair<int, string> name in dict_name)
            {
                // 1번은 상황 발생, 즉 초기값이라 버튼에 추가하지 않음.
                if(name.Key > 1) AddRadioButtonToPanel(wrap_kind_anomaly, name.Value, "Kind_Anomaly");
            }
        }
        // 라디오버튼 활성화, 비활성화
        private void RadioGroupChangeState(bool state = true)
        {
            foreach (UIElement child in wrap_kind_anomaly.Children)
            {
                if (child is RadioButton radioButton)
                {
                    // 라디오버튼 초기화
                    radioButton.IsChecked = false; 
                    radioButton.IsEnabled = state;  
                }
            }
        }

        public void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (baseImage.Source == null) return;

            double currentScale = scaleTransform.ScaleX;        // 현재 배율 저장 
            double zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double newScale = currentScale * zoomFactor;

            newScale = Math.Max(_minScale, Math.Min(newScale, 10.0));

            if (Math.Abs(newScale - currentScale) < 0.001) return;

            if (Math.Abs(newScale - _minScale) < 0.001)
            {
                scaleTransform.ScaleX = _minScale;
                scaleTransform.ScaleY = _minScale;
                scrollViewer.ScrollToHorizontalOffset(0);
                scrollViewer.ScrollToVerticalOffset(0);
            }
            else
            {
                Point mousePos = e.GetPosition(scrollViewer);

                double contentX = scrollViewer.HorizontalOffset + mousePos.X;
                double contentY = scrollViewer.VerticalOffset + mousePos.Y;

                double newContentX = contentX * (newScale / currentScale);
                double newContentY = contentY * (newScale / currentScale);

                double newHorizontalOffset = newContentX - mousePos.X;
                double newVerticalOffset = newContentY - mousePos.Y;

                scaleTransform.ScaleX = newScale;
                scaleTransform.ScaleY = newScale;

                this.UpdateLayout();

                scrollViewer.ScrollToHorizontalOffset(newHorizontalOffset);
                scrollViewer.ScrollToVerticalOffset(newVerticalOffset);
            }
            e.Handled = true;
        }
        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            FitToViewer();
        }

        private void FitToViewer()
        {
            if (baseImage.Source == null) return;

            double viewerWidth = scrollViewer.ActualWidth;
            double viewerHeight = scrollViewer.ActualHeight;

            double imageWidth = baseImage.Source.Width;
            double imageHeight = baseImage.Source.Height;

            double scaleX = viewerWidth / imageWidth;
            double scaleY = viewerHeight / imageHeight;

            double finalScale = Math.Min(scaleX, scaleY);

            this._minScale = finalScale;

            scaleTransform.ScaleX = finalScale;
            scaleTransform.ScaleY = finalScale;

            scrollViewer.ScrollToHorizontalOffset(0);
            scrollViewer.ScrollToVerticalOffset(0);
        }

        private void canvas_pin_add(object sender, MouseButtonEventArgs e)
        {
            if (!isPinModeEnabled || baseImage.Source == null) return;

            // 캔버스와 이미지의 실제 크기 
            double canvasWidth = mainCanvas.ActualWidth;
            double canvasHeight = mainCanvas.ActualHeight;
            double imageWidth = baseImage.ActualWidth;
            double imageHeight = baseImage.ActualHeight;

            // 캔버스와 이미지 사이의 상하좌우 여백 계산
            double offsetX = (canvasWidth - imageWidth) / 2;
            double offsetY = (canvasHeight - imageHeight) / 2;

            Point posImage = e.GetPosition(baseImage);
            double correctedX = posImage.X - offsetX;
            double correctedY = posImage.Y - offsetY;

            // 이미지 영역 밖에는 핀 추가 불가
            if (correctedX < 0 || correctedX > imageWidth || correctedY < 0 || correctedY > imageHeight)
            {
                Console.WriteLine("Out of Range");
                return;
            }

            double actualX = correctedX;
            double actualY = correctedY;

            // 핀 추가 사항 채우기
            Window_Add_Pin add_Pin = new Window_Add_Pin(this.MACList);      // MAC 주소 목록을 전달, 콤보박스에 추가
            if (add_Pin.ShowDialog() == true)
            {
                ClientPin pin_new = new ClientPin
                {
                    Idx = 0,
                    Name_Pin = add_Pin.TextBox_Name.Text,
                    MapIndex = this.MapSectors[this.idx_map].Idx,
                    Name_Location = add_Pin.TextBox_Location.Text,
                    PosX = actualX,
                    PosY = actualY,
                    MAC = add_Pin.MAC_Selected,
                    Date_Reg = DateTime.Now,
                    State_Anomaly = 1,
                    Name_Manager = add_Pin.TextBox_Manager.Text,
                    State_Connect = true
                };

                // 좌표값 재할당
                pin_new.PosX -= pin_new.pin_icon.Width / 2;
                pin_new.PosY -= pin_new.pin_icon.Height / 2;

                PinAddToCanvas(pin_new);

                // 핀 추가 종료
                PinModeButton.IsChecked = false; 
                this.pin_toggle_click(PinModeButton, null);

                // 서버에 해당 핀 정보 전송
            }
        }

        private void PinAddToCanvas(ClientPin pin_new, bool add_list = false)
        {
            pin_new.AddHandler(ClientPin.PinClickedEvent, new RoutedEventHandler(ClientPin_PinClicked));    // 이벤트 핸들러 등록, 핀 클릭 시 이벤트 발생
            pin_new.ChangeColorMode(1); // 기본 모드로 설정

            Canvas.SetLeft(pin_new, pin_new.PosX);
            Canvas.SetTop(pin_new, pin_new.PosY);

            mainCanvas.Children.Add(pin_new);
            if(add_list) this.PinList.Add(pin_new);      // 핀 목록에 추가
        }

        private void pin_toggle_click(object sender, RoutedEventArgs e)
        {
            this.isPinModeEnabled = PinModeButton.IsChecked == true;

            if (isPinModeEnabled)
            {
                mainCanvas.Cursor = Cursors.Cross;
                PinModeButton.Content = "취소";
            }
            else
            {
                mainCanvas.Cursor = null;
                PinModeButton.Content = "핀 추가";
            }
        }

        private void ScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (baseImage.Source != null && Math.Abs(scaleTransform.ScaleX - _minScale) < 0.001)
            {
                this.FitToViewer();
            }
        }

        // 메뉴바 기능 구현
        public void Click_Exit(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("정말로 종료할까요?", "프로그램 종료", MessageBoxButton.OKCancel);  // 첫번째 : 정보, 2번째 : 제목, 3번째 : 버튼 
            if (result == MessageBoxResult.OK)
            {
                this.Close();
            }
        }

        public void Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Test");
        }
        // 핀 목록 창 띄우기, 변경사항을 서버에 전송
        private void ClickManagePin(object sender, RoutedEventArgs e)
        {
            Window_Manage_Pin manage_Pin = new Window_Manage_Pin(this.PinList, this.MapSectors);
            if (manage_Pin.ShowDialog() == true)
            {
                // 백업본 생성
                List<ClientPin> backups = new List<ClientPin>();
                foreach (ClientPin pin in this.PinList)
                {

                }

                // 기존 핀들 전부 다 삭제
                this.mainCanvas.Children.Clear();
                this.PinList = manage_Pin.PinList_Origin;

                // 변경된 목록대로 핀 재배치
                foreach (ClientPin pin_new in this.PinList)
                {
                    PinAddToCanvas(pin_new);        // 핀 리스트에는 추가하지않고 캔버스에 추가
                }

                // 변경사항을 서버에 전송
            }
        }

        // 지도 페이지 변경, 변경 시 핀의 보임 여부 설정
        private void pb_map_prev_Click(object sender, RoutedEventArgs e)
        {
            if(this.MapSectors.Count > 0)
            {
                this.idx_map--;
                if (this.idx_map < 0)
                {
                    this.idx_map = this.MapSectors.Count - 1;
                }

                // 핀 보임 여부 설정
                foreach (var child in mainCanvas.Children)
                {
                    if (child is ClientPin pin)
                    {
                        pin.Visibility = pin.MapIndex == this.MapSectors[this.idx_map].Idx ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                this.ResetDetailPanel(); // 세부사항 패널 초기화
                LoadImageSafely(System.IO.Path.Combine(this.path_maps, this.MapSectors[this.idx_map].Path));
                this.FitToViewer();
            }
        }
        private void pb_map_next_Click(object sender, RoutedEventArgs e)
        {
            if(this.MapSectors.Count > 0)
            {
                this.idx_map++;     // 지도 목록 인덱스 증가(표시되는 인덱스가 아닌 컬렉션 인덱스)
                if (this.idx_map > this.MapSectors.Count - 1)
                {
                    this.idx_map = 0;
                }

                // 핀 보임 여부 설정
                foreach (var child in mainCanvas.Children)
                {
                    if (child is ClientPin pin)
                    {
                        pin.Visibility = pin.MapIndex == this.MapSectors[this.idx_map].Idx ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                this.ResetDetailPanel(); // 세부사항 패널 초기화
                LoadImageSafely(System.IO.Path.Combine(this.path_maps, this.MapSectors[this.idx_map].Path));
                this.FitToViewer();
            }
        }
        // 핀 클릭 이벤트 핸들러
        private void ClientPin_PinClicked(object sender, RoutedEventArgs e)
        {
            ClientPin clickedPin = e.OriginalSource as ClientPin;

            if (clickedPin != null)
            {
                // 핀 클릭 시 세부사항 패널 활성화
                label_data_pin.Content = clickedPin.Idx;
                label_data_name.Content = clickedPin.Name_Pin;
                label_data_map.Content = MapSectors.FirstOrDefault(sector => sector.Idx == clickedPin.MapIndex)?.Name;
                label_data_loc.Content = clickedPin.Name_Location;
                label_data_manager.Content = clickedPin.Name_Manager;
                // 해당 클라이언트가 접속하지 않았다면
                if (!clickedPin.State_Connect)
                {
                    label_data_state.Content = "서버와 연결되지 않음.";
                }
                else
                {
                    label_data_state.Content = clickedPin.State_Active ? "작동 중" : "대기 중";
                    pb_state_active.Content = clickedPin.State_Active ? "정지" : "작동";
                    pb_state_active.IsEnabled = true;

                    // 이상발생 목록 확인 후 가장 최신 핀 정보로 업데이트, 없으면 초기화
                    if (clickedPin.State_Anomaly > 0)
                    {
                        for (int i = this.List_Daily_Anomaly.Count - 1; i >= 0; i--)
                        {
                            if (this.List_Daily_Anomaly[i].Idx_Pin == this.idx_map)
                            {
                                AnomalyLog log = this.List_Daily_Anomaly[i];

                                label_start_datetime.Content = log.Time_Start.ToString("F");
                                label_proc_datetime.Content = log.Time_End.ToString("F");
                                RadioGroupChangeState();
                                textbox_proc_manager.Text = log.Worker;
                                textbox_proc_memo.Text = log.Memo;
                            }
                        }
                    }
                }

                // 지도 역시 특정 페이지로 전환
                this.idx_map = clickedPin.MapIndex;

                foreach (var child in mainCanvas.Children)
                {
                    if (child is ClientPin pin)
                    {
                        pin.Visibility = pin.MapIndex == this.MapSectors[this.idx_map].Idx ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                this.ResetDetailPanel(); // 세부사항 패널 초기화
                this.LoadImageSafely(System.IO.Path.Combine(this.path_maps, this.MapSectors[this.idx_map].Path));
                this.FitToViewer();
            }
        }
        // 화면 전환 시 세부사항 패널 초기화
        private void ResetDetailPanel()
        {
            label_data_map.ClearValue(ContentProperty);
            label_data_pin.ClearValue(ContentProperty);
            label_data_name.ClearValue(ContentProperty);
            label_data_loc.ClearValue(ContentProperty);
            label_data_manager.ClearValue(ContentProperty);
            label_data_state.ClearValue(ContentProperty);
            pb_state_active.Content = "";
            pb_state_active.IsEnabled = false;
        }
        // 상황 패널 초기화
        private void ResetDetailPanel_Situation(WrapPanel parentPanel)
        {
            label_start_datetime.ClearValue(ContentProperty);
            label_proc_datetime.ClearValue(ContentProperty);
            textbox_proc_memo.Text = "";
            textbox_proc_manager.Text = "";
            
            // 모든 라디오버튼 선택 해제
            foreach(object child in parentPanel.Children)
            {
                RadioButton? radioButton = child as RadioButton;
                radioButton.IsChecked = false;
            }
        }

        private void pbStateActiveAll_Click(object sender, RoutedEventArgs e)
        {
            OnStateActiveAll(true);
        }

        private void pbStateDectiveAll_Click(object sender, RoutedEventArgs e)
        {
            OnStateActiveAll(false);
        }

        // 전체 동작, 전체 정지 버튼 클릭 시 실행
        private void OnStateActiveAll(bool active)
        {
            WorkItem send_item = new();
            send_item.Protocol = "1-1-2";
            send_item.JsonData["STATE_ACTIVE"] = active;

            this.ExcuteCommand_Send(send_item);
        }

        private void pb_proc_init_Click(object sender, RoutedEventArgs e)
        {
            ResetDetailPanel_Situation(wrap_kind_anomaly);
        }
        // 이미지 불러오기 메서드
        public void LoadImageSafely(string relativeImagePath)
        {
            string fullImagePath = System.IO.Path.Combine(this.path_maps, relativeImagePath);

            if (!File.Exists(fullImagePath))
            {
                Console.WriteLine($"경고: 이미지를 찾을 수 없습니다 - {fullImagePath}");
                baseImage.Source = null;
                return;
            }

            BitmapImage bitmap = new BitmapImage();
            try
            {
                using (FileStream stream = new FileStream(fullImagePath, FileMode.Open, FileAccess.Read))
                {
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                } 

                baseImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"이미지 로드 중 오류 발생: {fullImagePath} - {ex.Message}");
                baseImage.Source = null; // 오류 시 이미지 소스 클리어
            }
        }
        // 개별 작동상태에 대한 상태 전환 요청
        private void pb_state_active_Click(object sender, RoutedEventArgs e)
        {
            // 현재 클릭되어 우측에 표시된 핀에 대하여 라벨로 상태 확인 후 작동, 대기 상태 전환 요청

            // 현재 클릭된 핀 번호 및 STATE_ACTIVE 획득
            

        }
    }
}