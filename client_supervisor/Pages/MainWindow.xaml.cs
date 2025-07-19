using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
        private TcpClientService _clientService;
        private IPAddress_Local iPAddress;

        private bool isPinModeEnabled { get; set; }
        private double _minScale = 1.0;

        /*-------------------------------------------------------------------*/
        // 각종 변수 선언
        // 지도 목록
        public ObservableCollection<MapSector> MapSectors { get; set; } = new ObservableCollection<MapSector>();
        private int idx_map = 0;

        // 핀 목록
        public ObservableCollection<ClientPin> PinList { get; set; } = new ObservableCollection<ClientPin>();

        // 현재 접속된 클라이언트(마이크) 목록
        public ObservableCollection<string> MACList { get; set; } = new ObservableCollection<string>();

        // 이상 여부 종류
        public List<string> List_Kind_Anomaly { get; set; } = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
            InitializeTimer();
            InitializeClientService();

            this.isPinModeEnabled = false;      // 핀 추가 값 초기화

            //this.list_kind_abnormal = new List<string>();
            //this.list_kind_abnormal.Add("과부하");
            //this.list_kind_abnormal.Add("문제해결");
            //this.list_kind_abnormal.Add("수리불가");
            //this.CreateRadioButtons_Click(this.list_kind_abnormal);

            this.load_serveraddr(this.path_server);
            this.MapSectors = this.load_maplist(true);      // 도면 불러오기

        }
        /*-------------------------------------------------------------------*/
        // 로컬 json 파일 제어
        // maps 폴더에 저장된 메타데이터 불러오기
        private ObservableCollection<MapSector> load_maplist(bool showimage = false)
        {
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
                baseImage.Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps", mapSectors.First().Path), UriKind.Absolute));
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
            var originalMapSectors = new ObservableCollection<MapSector>(this.MapSectors.Select(m => new MapSector
            {
                Idx = m.Idx,
                Name = m.Name,
                Path = m.Path,
                SizeB = m.SizeB,
                Path_Origin = m.Path_Origin // Path_Origin은 원본 비교에 사용될 수 있습니다.
                                            // 다른 필요한 속성들도 복사
            }));

            Window_Manage_Map window_Manage_Map = new Window_Manage_Map(this.MapSectors);
            if (window_Manage_Map.ShowDialog() == true)
            {
                // 캡쳐된 MapSectors를 원본 MapSectors로 덮어쓰기
                this.MapSectors = window_Manage_Map.MapSectors;
                Console.WriteLine($"변경된 MapSectors : {this.MapSectors.Count}");

                // 1. 삭제된 파일 처리
                Console.WriteLine($"\n1. 삭제된 파일 처리 시도:");
                foreach (var item in originalMapSectors)
                {
                    Console.WriteLine($"  현재 항목 : {item.Name} ({item.Path})");
                    // 캡쳐 기준 해당 항목이 없는 경우 삭제
                    if (!this.MapSectors.Contains(item))
                    {
                        string fullPathToDelete = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps", item.Path);
                        File.Delete(fullPathToDelete);
                        Console.WriteLine($"  삭제 변동사항 발생, 파일 삭제 시도 : {item.Name} ({fullPathToDelete})");
                    }
                }

                // 2. 새로 추가된 파일 처리
                Console.WriteLine($"\n2. 새로 추가된 파일 처리 시도:");
                foreach (var item in this.MapSectors)
                {
                    Console.WriteLine($"  현재 항목 : {item.Name} ({item.Path})");
                    // 캡쳐 기준 해당 항목이 없는 경우 또는 캡쳐 기준 갯수가 하나도 없는 경우 추가
                    if (!originalMapSectors.Contains(item))
                    {
                        string targetFilePath = System.IO.Path.Combine(this.path_maps, System.IO.Path.GetFileName(item.Path_Origin));
                        if (File.Exists(item.Path_Origin))
                        {
                            File.Copy(item.Path_Origin, targetFilePath, true);
                        }
                        Console.WriteLine($"  추가 변동사항 발생, 항목 추가 및 파일 복사 시도 : {item.Name} ({targetFilePath})");
                    }
                }

                // 3. 메타 JSON 파일 업데이트 (순서 변경 및 속성 업데이트 포함)
                Console.WriteLine($"\n3. 메타 JSON 파일 업데이트 시도:");
                List<object> list_json = new List<object>();

                foreach (MapSector mapData in this.MapSectors)
                {
                    Dictionary<string, object> dict_meta = new Dictionary<string, object>();
                    dict_meta.Add("IDX", mapData.Idx);
                    dict_meta.Add("NAME", mapData.Name);
                    dict_meta.Add("PATH", mapData.Path); // 업데이트된 Path를 사용

                    string fullPathForSize = System.IO.Path.Combine(this.path_maps, mapData.Path);

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

            this.load_maplist(true);      // 도면 목록 다시 불러오기
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
            // 서버와 연결 시도
            this._clientService.ConnectAsync(this.iPAddress.IP, this.iPAddress.Port);

            // 서버 연결 완료 시 연결 프로토콜 송신, 자동으로 순차적으로 실행
            Protocol protocol = new Protocol();
        }

        private void DisconnTCP_Click(object sender, RoutedEventArgs e)
        {
            this._clientService.Dispose();
            this.MapSectors.Clear();        // 도면 초기화
            baseImage.Source = null;        // 도면 이미지 초기화
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
        private void InitializeClientService()
        {
            _clientService = new TcpClientService();
            // TcpClientService에서 발생하는 이벤트를 구독합니다.
            _clientService.ConnectionStatusChanged += ClientService_ConnectionStatusChanged;
            _clientService.MessageReceived += ClientService_MessageReceived;
            _clientService.ErrorOccurred += ClientService_ErrorOccurred;
        }
        // --- TcpClientService 이벤트 핸들러 ---
        private void ClientService_ConnectionStatusChanged(object sender, bool isConnected)
        {
            // UI 스레드에서 UI를 업데이트합니다.
            Dispatcher.Invoke(() =>
            {
                if (isConnected)
                {
                    Header_Conn.IsEnabled = false;
                    Header_Disconn.IsEnabled = true;
                }
                else
                {
                    Header_Conn.IsEnabled = true;
                    Header_Disconn.IsEnabled = false;
                }
                UpdateStatus(isConnected ? "연결됨" : "연결 끊김");
            });
        }

        // 수신된 데이터 보관
        private void ClientService_MessageReceived(object sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                // 여기서 데이터 파싱하기
            });
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
                //Foreground = Brushes.DarkBlue // 글자 색상 설정 (선택 사항)
            };

            // 이벤트 핸들러 추가 (선택 사항)
            newRadioButton.Checked += RadioButton_Checked;
            newRadioButton.IsEnabled = false;
            parentPanel.Children.Add(newRadioButton); // 패널에 라디오 버튼 추가
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
        private void CreateRadioButtons_Click(List<string> list_name)
        {
            wrap_kind_anomaly.Children.Clear();

            foreach (string name in list_name)
            {
                AddRadioButtonToPanel(wrap_kind_anomaly, name, "Kind_Anomaly");
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
                    Idx = this.PinList.Count + 1,
                    Name = add_Pin.TextBox_Name.Text,
                    MapIndex = this.MapSectors[this.idx_map].Idx,
                    Name_Location = add_Pin.TextBox_Location.Text,
                    PosX = actualX,
                    PosY = actualY,
                    MAC = add_Pin.MAC_Selected,
                    Date_Reg = DateTime.Now,
                    State_Anomaly = 0,
                    Name_Manager = add_Pin.TextBox_Manager.Text
                };
                pin_new.AddHandler(ClientPin.PinClickedEvent, new RoutedEventHandler(ClientPin_PinClicked));    // 이벤트 핸들러 등록, 핀 클릭 시 이벤트 발생
                pin_new.ChangeColorMode(0); // 기본 모드로 설정

                Canvas.SetLeft(pin_new, actualX - pin_new.Width / 2);
                Canvas.SetTop(pin_new, actualY - pin_new.Height / 2);

                mainCanvas.Children.Add(pin_new);
                this.PinList.Add(pin_new);      // 핀 목록에 추가

                // 핀 추가 종료
                PinModeButton.IsChecked = false;
                this.pin_toggle_click(PinModeButton, null);

                // 서버에 해당 핀 정보 전송
            }
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
                // 기존 핀들 전부 다 삭제
                this.mainCanvas.Children.Clear();
                this.PinList = manage_Pin.PinList_Origin;

                // 변경된 목록대로 핀 재배치
                foreach (ClientPin pin_new in this.PinList)
                {
                    pin_new.AddHandler(ClientPin.PinClickedEvent, new RoutedEventHandler(ClientPin_PinClicked));    // 이벤트 핸들러 등록, 핀 클릭 시 이벤트 발생
                    pin_new.ChangeColorMode(0); // 기본 모드로 설정

                    Canvas.SetLeft(pin_new, pin_new.PosX);
                    Canvas.SetTop(pin_new, pin_new.PosY);

                    mainCanvas.Children.Add(pin_new);
                }

                // 변경사항을 서버에 전송, 전체 목록 전송
            }
        }

        // 지도 페이지 변경, 변경 시 핀의 보임 여부 설정
        private void pb_map_prev_Click(object sender, RoutedEventArgs e)
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
            baseImage.Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps", this.MapSectors[this.idx_map].Path), UriKind.Absolute));
            this.FitToViewer();
        }
        private void pb_map_next_Click(object sender, RoutedEventArgs e)
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
            baseImage.Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps", this.MapSectors[this.idx_map].Path), UriKind.Absolute));
            this.FitToViewer();
        }
        // 핀 클릭 이벤트 핸들러
        private void ClientPin_PinClicked(object sender, RoutedEventArgs e)
        {
            ClientPin clickedPin = e.OriginalSource as ClientPin;

            if (clickedPin != null)
            {
                // 핀 클릭 시 세부사항 패널 활성화
                label_data_pin.Content = clickedPin.Idx;
                label_data_map.Content = MapSectors.FirstOrDefault(sector => sector.Idx == clickedPin.MapIndex)?.Name;
                label_data_loc.Content = clickedPin.Name_Location;
                label_data_manager.Content = clickedPin.Name_Manager;
                label_data_state.Content = clickedPin.State_Active ? "활성화" : "비활성화";
                // label_data_abnormal.Content = this.List_Kind_Anomaly[clickedPin.State_Anomaly];
                label_data_abnormal.Content = 0;

                // 이상발생 목록 확인 후 가장 최신 핀 정보로 업데이트, 없으면 초기화
            }
        }
        // 화면 전환 시 세부사항 패널 초기화
        private void ResetDetailPanel()
        {
            label_data_pin.ClearValue(ContentProperty);
            label_data_map.ClearValue(ContentProperty);
            label_data_loc.ClearValue(ContentProperty);
            label_data_manager.ClearValue(ContentProperty);
            label_data_state.ClearValue(ContentProperty);
            label_data_abnormal.ClearValue(ContentProperty);
        }
        // 상황 패널 초기화
        private void ResetDetailPanel_Situation()
        {

        }
    }
}