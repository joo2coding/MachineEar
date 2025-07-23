using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
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

namespace client_supervisor
{

    public enum STATE_COLOR { OFFLINE, STANDBY, WORKING, ANOMALY }
    /// <summary>
    /// UserControl_Pin.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class ClientPin : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private int _idx;
        public int Idx
        {
            get { return _idx; }
            set
            {
                if (_idx != value)
                {
                    _idx = value;
                    OnPropertyChanged(nameof(Idx));
                }
            }
        }

        private string _name_pin;
        public string Name_Pin
        {
            get { return _name_pin; }
            set
            {
                if (_name_pin != value)
                {
                    _name_pin = value;
                    OnPropertyChanged(nameof(Name_Pin));
                }
            }
        }

        private int _mapindex;
        public int MapIndex
        {
            get { return _mapindex; }
            set
            {
                if (_mapindex != value)
                {
                    _mapindex = value;
                    OnPropertyChanged(nameof(MapIndex));
                }
            }
        }

        private double _posx;
        public double PosX
        {
            get { return _posx; }
            set
            {
                if (_posx != value)
                {
                    _posx = value;
                    OnPropertyChanged(nameof(PosX));
                }
            }
        }

        private double _posy;
        public double PosY
        {
            get { return _posy; }
            set
            {
                if (_posy != value)
                {
                    _posy = value;
                    OnPropertyChanged(nameof(PosY));
                }
            }
        }

        private string _mac;
        public string MAC
        {
            get { return _mac; }
            set
            {
                if (_mac != value)
                {
                    _mac = value;
                    OnPropertyChanged(nameof(MAC));
                }
            }
        }

        private DateTime _date_reg;
        public DateTime Date_Reg
        {
            get { return _date_reg; }
            set
            {
                if (_date_reg != value)
                {
                    _date_reg = value;
                    OnPropertyChanged(nameof(Date_Reg));
                }
            }
        }

        private string _name_manager;
        public string Name_Manager
        {
            get { return _name_manager; }
            set
            {
                if (_name_manager != value)
                {
                    _name_manager = value;
                    OnPropertyChanged(nameof(Name_Manager));
                }
            }
        }

        private string _name_location;
        public string Name_Location
        {
            get { return _name_location; }
            set
            {
                if (_name_location != value)
                {
                    _name_location = value;
                    OnPropertyChanged(nameof(Name_Location));
                }
            }
        }

        private bool _state_active;
        public bool State_Active
        {
            get { return _state_active; }
            set
            {
                if (_state_active != value)
                {
                    _state_active = value;
                    OnPropertyChanged(nameof(State_Active));
                }
            }
        }

        private STATE_COLOR _mode_color;
        public STATE_COLOR Mode_Color
        {
            get { return _mode_color; }
            set
            {
                if (_mode_color != value)
                {
                    _mode_color = value;
                    OnPropertyChanged(nameof(Mode_Color));
                }
            }
        }
        private bool _is_selected;
        public bool IsSelected
        {
            get { return _is_selected; }
            set
            {
                if (_is_selected != value)
                {
                    _is_selected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
        private bool _state_connect;
        public bool State_Connect
        {
            get { return _state_connect; }
            set
            {
                if (_state_connect != value)
                {
                    _state_connect = value;
                    OnPropertyChanged(nameof(State_Connect));
                }
            }
        }

        public ClientPin()
        {
            InitializeComponent();
            this.Mode_Color = 0;
        }

        public void ChangeStrokeColor(String color)     // 스트로크 색상 변경
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                Console.WriteLine("Please Enter a Color Name or Hex Code");
                return;
            }

            try
            {
                var conv = new BrushConverter();
                var brush = (Brush)conv.ConvertFromString(color);
                pin_icon.Stroke = brush;
            }
            catch (FormatException)
            {
                Console.WriteLine($"{color} is not a valid Color Name or Hex Code");
            }
        }
        public void ChangeFillColor(String color)     // 내부 채움 색상 변경
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                Console.WriteLine("Please Enter a Color Name or Hex Code");
                return;
            }

            try
            {
                var conv = new BrushConverter();
                var brush = (Brush)conv.ConvertFromString(color);
                pin_icon.Fill = brush;
            }
            catch (FormatException)
            {
                Console.WriteLine($"{color} is not a valid Color Name or Hex Code");
            }
        }

        public void ChangeColorMode(STATE_COLOR mode)
        {
            // mode 0: 오프라인 상태, mode 1: 대기 상태 mode 2 : 작업중, mode 3: 이상 상태 색상
            this.Mode_Color = mode;
            switch (mode)
            {
                case STATE_COLOR.OFFLINE:
                    ChangeStrokeColor("Black");
                    ChangeFillColor("DarkGray");
                    break;
                case STATE_COLOR.STANDBY:
                    ChangeStrokeColor("Black");
                    ChangeFillColor("Green");
                    break;
                case STATE_COLOR.WORKING:
                    ChangeStrokeColor("Black");
                    ChangeFillColor("Yellow");
                    break;
                case STATE_COLOR.ANOMALY:
                    ChangeStrokeColor("Black");
                    ChangeFillColor("Red");
                    break;
                default:
                    Console.WriteLine("Invalid mode. Please use 0, 1, or 2.");
                    break;
            }
        }

        // 라우티드 이벤트 정의
        public static readonly RoutedEvent PinClickedEvent = EventManager.RegisterRoutedEvent(
            "PinClicked",                     // 이벤트 이름
            RoutingStrategy.Bubble,           // 이벤트 라우팅 전략 (버블링: 자식에서 부모로 전파)
            typeof(RoutedEventHandler),       // 이벤트 핸들러 델리게이트 타입 (일반적인 RoutedEventHandler)
            typeof(ClientPin)                 // 이 이벤트를 소유하는 타입
        );

        // 외부에서 이벤트를 구독할 수 있도록 핸들러 추가/제거 메서드 제공
        public event RoutedEventHandler PinClicked
        {
            add { AddHandler(PinClickedEvent, value); }
            remove { RemoveHandler(PinClickedEvent, value); }
        }

        // 핀 클릭 시 이벤트 발생 메서드
        void RaisePinClickedEvent()
        {
            // 이벤트 아규먼트 생성. 이벤트 소스를 현재 ClientPin 인스턴스로 설정.
            RoutedEventArgs newEventArgs = new RoutedEventArgs(PinClickedEvent, this);
            RaiseEvent(newEventArgs); // 이벤트를 발생시키고 라우팅 시작
        }

        private void pin_icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            RaisePinClickedEvent();
            // 이벤트가 다른 상위 요소로 전파되지 않도록 처리
            e.Handled = true;
        }

        public ClientPin Copy()
        {
            ClientPin copy = new ClientPin();

            copy.Idx = this.Idx;
            copy.Name_Pin = this.Name_Pin;
            copy.MapIndex = this.MapIndex;
            copy.PosX = this.PosX;
            copy.PosY = this.PosY;
            copy.MAC = this.MAC;
            copy.Date_Reg = this.Date_Reg;
            copy.Name_Manager = this.Name_Manager;
            copy.Name_Location = this.Name_Location;
            copy.State_Active = this.State_Active;
            copy.Mode_Color = this.Mode_Color;
            copy.IsSelected = this.IsSelected;
            copy.State_Connect = this.State_Connect;

            if (this.pin_icon != null && copy.pin_icon != null)
            {
                if (this.pin_icon.Stroke != null)
                    copy.pin_icon.Stroke = this.pin_icon.Stroke.Clone(); // Freezable 객체의 깊은 복사
                if (this.pin_icon.Fill != null)
                    copy.pin_icon.Fill = this.pin_icon.Fill.Clone();     // Freezable 객체의 깊은 복사
            }

            return copy;
        }
    }

    // ClientPin의 equal과 gethashcode가 봉인되어 있어서 비교하는 클래스 생성
    public class ClientPinComparer : IEqualityComparer<ClientPin>
    {
        // 1. IEqualityComparer 인터페이스를 위한 Equals (Idx, MAC만 비교 - "식별자 동일성")
        public bool Equals(ClientPin x, ClientPin y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;

            return x.Idx == y.Idx &&
                   string.Equals(x.MAC, y.MAC, StringComparison.OrdinalIgnoreCase);
        }

        // 2. IEqualityComparer 인터페이스를 위한 GetHashCode (Idx, MAC만으로 해시 생성)
        public int GetHashCode(ClientPin obj)
        {
            if (obj == null) return 0;
            return HashCode.Combine(obj.Idx, obj.MAC?.ToLowerInvariant());
        }

        // 3. 새로운 메서드: 모든 속성의 "내용"을 비교하는 메서드 (추가)
        public bool AreContentsEqual(ClientPin x, ClientPin y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;

            // 모든 관련 속성들을 비교하여 내용 동일성 판단
            return x.Idx == y.Idx &&
                   string.Equals(x.MAC, y.MAC, StringComparison.OrdinalIgnoreCase) && // MAC은 대소문자 무시
                   string.Equals(x.Name_Pin, y.Name_Pin, StringComparison.Ordinal) && // Name_Pin 비교 (대소문자 구분)
                   string.Equals(x.Name_Location, y.Name_Location, StringComparison.Ordinal) && // Name_Location 비교
                   string.Equals(x.Name_Manager, y.Name_Manager, StringComparison.Ordinal) && // Name_Manager 비교
                   x.MapIndex == y.MapIndex &&
                   x.PosX == y.PosX &&
                   x.PosY == y.PosY &&
                   x.Date_Reg == y.Date_Reg &&
                   x.State_Active == y.State_Active &&
                   x.Mode_Color == y.Mode_Color &&
                   x.IsSelected == y.IsSelected &&
                   x.State_Connect == y.State_Connect;
        }
    }
}
