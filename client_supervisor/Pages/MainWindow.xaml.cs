using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
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
        string path_map_path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps/path_maps.json");

        private DispatcherTimer? timer_curr;
        private TcpClientService clientService;
        private IPAddress_Local iPAddress;

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
        List<MapSector> Map_Modified = new();

        // 핀 목록
        public ObservableCollection<ClientPin> PinList { get; set; } = new ObservableCollection<ClientPin>();
        public List<ClientPin> PinList_Modified { get; set; } = new List<ClientPin>();
        public List<ClientPin> PinList_Add { get; set; } = new List<ClientPin>();
        public List<int> PinList_Remove { get; set; } = new List<int>();

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

            //Header_Manage_Map.IsEnabled = true;
            //this.MapSectors = this.load_maplist(true);      // 지도 목록 불러오기
        }
        /*-------------------------------------------------------------------*/
        // 로컬 json 파일 제어
        
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
        private async void ClickManageMap(object sender, RoutedEventArgs e)
        {
            this.Map_Removed.Clear();   // 삭제된 맵의 Idx를 저장
            this.Map_Add.Clear();       // 새로 추가된 맵 객체를 저장
            this.Map_Modified.Clear();  // 수정된 맵 객체를 저장
            MapSectorComparer _comparer = new MapSectorComparer();
            WorkItem item = new WorkItem();

            Window_Manage_Map window_Manage_Map = new Window_Manage_Map(this.MapSectors);
            if (window_Manage_Map.ShowDialog() == true)
            {
                // 캡쳐된 MapSectors를 원본 MapSectors로 덮어쓰기
                ObservableCollection<MapSector> MapSectors_recv = window_Manage_Map.MapSectors;
                HashSet<MapSector> mapRecvHashSet = new HashSet<MapSector>(MapSectors_recv, _comparer);

                // 수정 및 삭제
                foreach (MapSector originalMap in this.MapSectors)
                {
                    MapSector correspondingMapInRecv = mapRecvHashSet.FirstOrDefault(m => _comparer.Equals(m, originalMap));

                    if (correspondingMapInRecv == null)
                    {
                        // correspondingMapInRecv가 null이면, originalMap은 최신 목록에 없습니다. (삭제됨)
                        this.Map_Removed.Add(originalMap.Idx);
                        Console.WriteLine($"  [삭제 감지] Idx: {originalMap.Idx}, Name: {originalMap.Name_Map}");
                    }
                    else
                    {
                        // 식별자는 같지만, 내용이 다른 경우 (수정됨)
                        if (!_comparer.AreContentsEqual(originalMap, correspondingMapInRecv))
                        {
                            this.Map_Modified.Add(correspondingMapInRecv); // 최신 수정된 맵 추가
                            Console.WriteLine($"  [수정 감지] Idx: {originalMap.Idx}, Original Name: {originalMap.Name_Map}, New Name: {correspondingMapInRecv.Name_Map}");
                        }
                    }
                }

                // 2. 새로 추가된 파일 처리
                Console.WriteLine($"\n2. 새로 추가된 파일 처리 시도:");
                this.Map_Add = compare_maplist_add(this.MapSectors, MapSectors_recv); // 새로 추가된 맵 목록을 가져옴, NUM_MAP이 없음, 0으로 처리되어있음

                // 지도 목록 수정 요청
                item.Protocol = "1-3-1";
                await this.ExcuteCommand_Send(item);

                // 3. 메타 JSON 파일 업데이트 (순서 변경 및 속성 업데이트 포함)
                this.MapSectors = MapSectors_recv;

                // 4. 서버에 추가된 도면 파일 전송
                foreach(MapSector map_add in this.Map_Add)
                {
                    item.Protocol = "1-3-2";
                    item.JsonData["INDEX_MAP"] = map_add.Idx;
                    item.JsonData["NAME_MAP"] = map_add.Name_Map;


                    JObject obj = new JObject();
                    obj["NAME"] = System.IO.Path.GetFileName(map_add.Path);
                    obj["SIZE"] = map_add.SizeB;

                    item.JsonData["__META__"] = obj;
                    item.BinaryData = File.ReadAllBytes(map_add.Path);

                    await this.ExcuteCommand_Send(item);
                }

                // 파일 저장
                this.save_maplist();

                // 지도 목록 다시 요청
                item.JsonData = new JObject();
                item.Protocol = "1-3-0";
                DataReceivedEventArgs send_130 = await this.ExcuteCommand_SendAndWait(item, item.Protocol);
                this.Act_SendAndRecv(send_130);

            }

            this.MapSectors = this.load_maplist(true);      // 도면 목록 다시 불러오기
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
            Header_Log.IsEnabled = true;

            Header_Conn.IsEnabled = false;
            Header_Disconn.IsEnabled = true;
            WorkItem send_item = new();

            // 접속 요청 송신 및 수신
            send_item.Protocol = "1-0-0";
            DataReceivedEventArgs send_100 = await this.ExcuteCommand_SendAndWait(send_item, send_item.Protocol);
            this.Act_SendAndRecv(send_100);

            // 연결이 유지될때만 아래 태스크 실행, 연결이 종료되면 수행 불가 
            if (this.clientService.IsConnected)
            {
                // 고장원인 목록 요청
                send_item.Protocol = "1-0-1";
                DataReceivedEventArgs send_101 = await this.ExcuteCommand_SendAndWait(send_item, send_item.Protocol);
                this.Act_SendAndRecv(send_101);

                // 이상 상태 처리 목록 요청
                send_item.Protocol = "1-0-2";
                DataReceivedEventArgs send_102 = await this.ExcuteCommand_SendAndWait(send_item, send_item.Protocol);
                this.Act_SendAndRecv(send_102);

                // 등록되지 않은 MAC 목록 요청
                send_item.Protocol = "1-0-3";
                DataReceivedEventArgs send_103 = await this.ExcuteCommand_SendAndWait(send_item, send_item.Protocol);
                this.Act_SendAndRecv(send_103);

                // 지도 목록 요청
                send_item.Protocol = "1-3-0";
                DataReceivedEventArgs send_130 = await this.ExcuteCommand_SendAndWait(send_item, send_item.Protocol);
                this.Act_SendAndRecv(send_130);

                Console.WriteLine($"추가 갯수 : {this.Map_Add.Count}");
                // 추가해야하는 지도가 있다면 서버에 요청
                foreach (MapSector map in this.Map_Add)
                {
                    send_item.Protocol = "1-3-3";
                    send_item.JsonData["NUM_MAP"] = map.Num_Map;
                    DataReceivedEventArgs send_133 = await this.ExcuteCommand_SendAndWait(send_item, send_item.Protocol);
                    this.Act_SendAndRecv(send_133);
                }

                // JSON 객체 초기화
                send_item.JsonData = new JObject();

                // 저장된 메타데이터 불러오기
                this.MapSectors = this.load_maplist(true);

                // 핀 목록 요청
                send_item.Protocol = "1-1-0";
                //await this.ExcuteCommand_Send(send_item);
                DataReceivedEventArgs send_110 = await this.ExcuteCommand_SendAndWait(send_item, send_item.Protocol);
                this.Act_SendAndRecv(send_110);
            }
        }
        // 서버 연결이 끊어졌을 때 실행되는 메서드
        private void OnDisconnectedFromServer()
        {
            Header_Manage_Map.IsEnabled = false;
            Header_Manage_Pin.IsEnabled = false;
            Header_Log.IsEnabled = false;

            this.PinList.Clear();
            this.MapSectors.Clear();
            this.mainCanvas.Children.Clear();
            this.baseImage.Source = null;

            this.ResetDetailPanel();
            this.DeactiveDetailPanel(wrap_kind_anomaly);
            this.DeleteRadioButtonToPanel(wrap_kind_anomaly);

            Header_Conn.IsEnabled = true;
            Header_Disconn.IsEnabled = false;

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

        private async void canvas_pin_add(object sender, MouseButtonEventArgs e)
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
                this.PinList_Add.Clear();

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
                pin_new.PosX -= pin_new.pin_icon.ActualWidth / 2;
                pin_new.PosY -= pin_new.pin_icon.ActualHeight / 2;

                PinAddToCanvas(pin_new);

                // 핀 추가 종료
                PinModeButton.IsChecked = false; 
                this.pin_toggle_click(PinModeButton, null);
                this.PinList_Add.Add(pin_new);      // 생성 목록에 넣기

                // 서버에 해당 핀 정보 전송
                WorkItem item = new WorkItem();
                item.Protocol = "1-3-4";
                await this.ExcuteCommand_Send(item);

                // 서버 적용 시간 유예
                Thread.Sleep(50);

                // MAC 리스트 재송신 요청
                item.Protocol = "1-0-3";
                DataReceivedEventArgs send_103 = await this.ExcuteCommand_SendAndWait(item, item.Protocol);
                this.Act_SendAndRecv(send_103);

                // 핀 목록 재요청
                item.Protocol = "1-1-0";
                DataReceivedEventArgs send_110 = await this.ExcuteCommand_SendAndWait(item, item.Protocol);
                this.Act_SendAndRecv(send_110);
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

        // 핀 목록 창 띄우기, 변경사항을 서버에 전송
        private async void ClickManagePin(object sender, RoutedEventArgs e)
        {
            // manage_Pin 다이얼로그를 띄워 핀 관리 작업을 수행합니다.
            Window_Manage_Pin manage_Pin = new Window_Manage_Pin(this.PinList, this.MapSectors);
            if (manage_Pin.ShowDialog() == true)
            {
                ObservableCollection<ClientPin> pin_recv = manage_Pin.PinList_Origin;
                ClientPinComparer _comparer = new ClientPinComparer();
                HashSet<ClientPin> pinRecvHashSet = new HashSet<ClientPin>(pin_recv, _comparer);

                this.PinList_Modified.Clear(); // 수정된 핀 객체를 저장할 리스트
                this.PinList_Remove.Clear();

                // --- 1. 기존 핀 목록(this.PinList)을 순회하며 삭제 및 수정된 핀을 확인합니다. ---
                foreach (ClientPin originalPin in this.PinList)
                {
                    // 현재 originalPin과 동일한 식별자(Idx, MAC)를 가진 핀이 pin_recv에 있는지 찾습니다.
                    ClientPin correspondingPinInRecv = pinRecvHashSet.FirstOrDefault(p => _comparer.Equals(p, originalPin));

                    if (correspondingPinInRecv == null)
                    {
                        this.PinList_Remove.Add(originalPin.Idx);
                    }
                    else
                    {
                        if (!_comparer.AreContentsEqual(originalPin, correspondingPinInRecv))
                        {
                            this.PinList_Modified.Add(correspondingPinInRecv);
                        }
                    }
                }

                // --- 캔버스 및 핀 목록 업데이트 ---
                // 기존 캔버스의 모든 핀 UI 요소를 삭제합니다.
                this.mainCanvas.Children.Clear();
                this.PinList = pin_recv;

                foreach (ClientPin pin_new in this.PinList)
                {
                    PinAddToCanvas(pin_new);
                }

                // --- 변경 사항을 서버에 전송 ---
                // 프로토콜 1-3-4로 변경 사항 전송 명령을 실행합니다.
                WorkItem item = new WorkItem();
                item.Protocol = "1-3-4";
                await this.ExcuteCommand_Send(item); // 비동기 전송

                // --- 핀 목록 재요청 ---
                // 프로토콜 1-1-0으로 서버에 핀 목록을 다시 요청합니다.
                item.Protocol = "1-1-0";
                DataReceivedEventArgs send_110 = await this.ExcuteCommand_SendAndWait(item, item.Protocol); // 비동기 전송 후 응답 대기
                this.Act_SendAndRecv(send_110); // 수신된 데이터를 처리합니다.
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
                label_data_map.Content = MapSectors.FirstOrDefault(sector => sector.Idx == clickedPin.MapIndex)?.Name_Map;
                label_data_loc.Content = clickedPin.Name_Location;
                label_data_manager.Content = clickedPin.Name_Manager;


                // 해당 클라이언트가 접속하지 않았다면
                if (!clickedPin.State_Connect)
                {
                    pb_state_active.Content = "";
                    pb_state_active.IsEnabled = false;
                    label_data_state.Content = "서버와 연결되지 않음";
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
                for (int i = 0; i < this.MapSectors.Count; i ++)
                {
                    if (this.MapSectors[i].Idx == clickedPin.MapIndex)
                    {
                        this.idx_map = i;
                        break;
                    }
                }

                foreach (var child in mainCanvas.Children)
                {
                    if (child is ClientPin pin)
                    {
                        pin.Visibility = pin.MapIndex == this.MapSectors[this.idx_map].Idx ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                this.LoadImageSafely(System.IO.Path.Combine(this.path_maps, this.MapSectors[this.idx_map].Path));
                //this.FitToViewer();
                activateDetailPanel(true);
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
        private void DeactiveDetailPanel(WrapPanel parentPanel)
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
        private async void OnStateActiveAll(bool active)
        {
            WorkItem send_item = new WorkItem();
            send_item.Protocol = "1-1-2";
            send_item.JsonData["STATE_ACTIVE"] = active;

            await this.ExcuteCommand_Send(send_item);
        }

        private void pb_proc_init_Click(object sender, RoutedEventArgs e)
        {
            DeactiveDetailPanel(wrap_kind_anomaly);
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
        private async void pb_state_active_Click(object sender, RoutedEventArgs e)
        {

            // 현재 클릭되어 우측에 표시된 핀에 대하여 라벨로 상태 확인 후 작동, 대기 상태 전환 요청
            Int32.TryParse(label_data_pin.Content.ToString(), out int num_pin);
            // 현재 클릭된 핀 번호 및 STATE_ACTIVE 획득
            for(int i = 0; i < this.PinList.Count; i ++)
            {
                // 목록의 핀 번호와 현재 선택된 핀 번호가 같다면
                if(this.PinList[i].Idx == num_pin)
                {
                    WorkItem item = new WorkItem();
                    item.Protocol = "1-1-1";
                    item.JsonData["NUM_PIN"] = num_pin;

                    // STATE_ACTIVE 상태 변경, bool이라서 반전 실행
                    this.PinList[i].State_Active = !this.PinList[i].State_Active;
                    item.JsonData["STATE_ACTIVE"] = this.PinList[i].State_Active;
                    
                    await this.ExcuteCommand_Send(item);      // 1-1-1 송신

                    // 핀 상태 변경 조회
                    item.Protocol = "1-1-0";
                    DataReceivedEventArgs send_110 = await this.ExcuteCommand_SendAndWait(item, item.Protocol); // 비동기 전송 후 응답 대기
                    this.Act_SendAndRecv(send_110); // 수신된 데이터를 처리합니다.

                    label_data_state.Content = this.PinList[i].State_Active ? "작동 중" : "대기 중";
                    pb_state_active.Content = this.PinList[i].State_Active ? "정지" : "작동";
                }
            }
        }
        // 이전 기록 창을 띄우기 위한 
        private void pb_log_Click(object sender, RoutedEventArgs e)
        {
            Window_Log_TotalAnomaly log_total = new Window_Log_TotalAnomaly();
            log_total.Req_Log_Date += this.req_log_total_datetime;
            if (log_total.ShowDialog() == true)
            {

            }
        }

        // 이전 기록 확인에서 날짜가 변경될 때 마다 창에서 값을 받아서 서버로 전송
        private void req_log_total_datetime(object sender, DateTime selected)
        {
            WorkItem item = new WorkItem();
            item.Protocol = "1-2-2";
            item.JsonData["DATE_REQ"] = selected.ToString();

            this.ExcuteCommand_Send(item);
        }
    }
}