using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace client_supervisor
{
    // 지도 목록
    public class MapSector : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        [JsonProperty("NUM_MAP")]
        private int _num_map;
        public int Num_Map
        {
            get { return _num_map; }
            set
            {
                if (_num_map != value)
                {
                    _num_map = value;
                    OnPropertyChanged(nameof(Num_Map));
                }
            }
        }

        [JsonProperty("INDEX")]
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

        [JsonProperty("NAME")]
        private string _name_map;
        public string Name_Map
        {
            get { return _name_map; }
            set
            {
                if (_name_map != value)
                {
                    _name_map = value;
                    OnPropertyChanged(nameof(Name_Map));
                }
            }
        }

        [JsonProperty("PATH")]
        public string Path { get; set; }

        [JsonProperty("SIZE")]
        public int SizeB { get; set; }

        [JsonIgnore]
        public string Path_Origin { get; set; }
        [JsonIgnore]
        public ICommand UpCommand { get; set; }
        [JsonIgnore]
        public ICommand DownCommand { get; set; }

        // 체크박스
        private bool _isSelected;
        [JsonIgnore]
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public override bool Equals(object? obj)
        {
            // 참조 동일성 확인: 같은 객체인 경우 true 반환
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            // null 확인: 비교 대상이 null인 경우 false 반환
            if (obj is null)
            {
                return false;
            }

            // 타입 확인: 비교 대상이 MapSector 타입이 아닌 경우 false 반환
            if (GetType() != obj.GetType())
            {
                return false;
            }

            // obj를 MapSector 타입으로 캐스팅
            MapSector? other = obj as MapSector;

            // 프로퍼티 비교: 모든 핵심 프로퍼티들이 같은지 확인
            return Idx == other.Idx;
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(Idx);
        }

        public MapSector Copy()
        {
            MapSector copy = new();

            copy.Name_Map = this.Name_Map;
            copy.Num_Map = this.Num_Map;
            copy.SizeB = this.SizeB;
            copy.Path = this.Path;
            copy.Idx = this.Idx;
            copy.IsSelected = this.IsSelected;

            return copy;
        }
    }

    public class MapSectorComparer : IEqualityComparer<MapSector>
    {
        public bool Equals(MapSector x, MapSector y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;

            return x.Idx == y.Idx; // Idx만으로 고유성 판단
        }

        public int GetHashCode(MapSector obj)
        {
            if (obj == null) return 0;
            return obj.Idx.GetHashCode();
        }

        // MapSector의 모든 속성 "내용"을 비교하는 메서드 (ClientPin처럼 Equals 오버라이딩이 안 된다면 필요)
        // 만약 MapSector의 Equals가 봉인되어 있다면 이 메서드를 사용하세요.
        public bool AreContentsEqual(MapSector x, MapSector y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;

            // MapSector.Equals에서 비교하는 모든 속성들을 여기에 추가
            return x.Idx == y.Idx &&
                   string.Equals(x.Name_Map, y.Name_Map, StringComparison.Ordinal) &&
                   x.SizeB == y.SizeB;
        }
    }

    // ICommand 인터페이스를 상속받는 클래스 생성
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }
}
