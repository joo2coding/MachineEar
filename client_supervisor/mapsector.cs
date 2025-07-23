using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace client_supervisor
{
    public partial class MainWindow
    {
        // NUM_MAP 부여
        private void resign_num_map(ObservableCollection<MapSector> src, ObservableCollection<MapSector> tgt) 
        {
            Console.WriteLine("지도 번호 재정렬");

            for (int i = 0; i < src.Count; i++)
            {
                for(int j = 0; j < tgt.Count; j++)
                {
                    if (src[j].Idx == tgt[i].Idx && src[j].Name_Map == tgt[i].Name_Map)
                    {
                        src[i].Num_Map = tgt[j].Num_Map;
                        break;
                    }
                }
            }

            this.save_maplist();
        }

        // maps 폴더에 저장된 메타데이터 불러오기
        private ObservableCollection<MapSector> load_maplist(bool showimage = false)
        {
            Console.WriteLine("지도 불러오기 시작");
            baseImage.Source = null;

            // maps 폴더가 루트에 존재하는지 확인
            if (!Directory.Exists(this.path_maps))
            {
                Directory.CreateDirectory(this.path_maps);
            }

            // map의 메타파일이 존재하는지 확인
            if (!File.Exists(this.path_map_meta))
            {
                Console.WriteLine("파일 없음, 새로 생성 : map_meta.json");
                File.WriteAllText(this.path_map_meta, "[]");     // 기본 객체 생성 후 파일 저장
            }

            // map의 경로파일이 존재하는지 확인
            if (!File.Exists(this.path_map_path))
            {
                Console.WriteLine("파일 없음, 새로 생성 : map_path.json");
                File.WriteAllText(this.path_map_path, "[]");     // 기본 객체 생성 후 파일 저장
            }

            ObservableCollection<MapSector> mapSectors = new ObservableCollection<MapSector>();

            string read_string = File.ReadAllText(this.path_map_meta);
            JArray? metaObject = JsonConvert.DeserializeObject<JArray>(read_string);
            // 경로 파일에서 경로를 읽어와서 mapSectors에 추가
    
            for (int j = 0; j < metaObject.Count; j++)
            {
                MapSector map_new = new MapSector();

                map_new.Num_Map = metaObject[j]["NUM_MAP"].Value<int>();
                map_new.Name_Map = metaObject[j]["NAME"].Value<string>();
                map_new.Idx = metaObject[j]["IDX"].Value<int>();


                mapSectors.Add(map_new);
            }
            
            string read_path = File.ReadAllText(this.path_map_path);
            JArray? pathObject = JsonConvert.DeserializeObject<JArray>(read_path);
            // 경로 파일에서 경로를 읽어와서 mapSectors에 추가
            for (int i = 0; i < mapSectors.Count; i++)
            {
                for (int j = 0; j < pathObject.Count; j++)
                {
                    if( mapSectors[i].Num_Map == (int)pathObject[j]["NUM_MAP"])
                    {
                        mapSectors[i].Path = (string)pathObject[j]["PATH"];
                        mapSectors[i].SizeB = (int)pathObject[j]["SIZE"];
  
                        break;  // 경로를 찾았으면 더 이상 반복할 필요 없음
                    }
                }
            }

            // 이미지 목록을 불러왔으면, 이미지를 출력
            if (showimage && mapSectors.Count > 0)
            {
                LoadImageSafely(Path.Combine(path_maps, mapSectors.First().Path));
                this.idx_map = 0;
                this.FitToViewer();
            }

            return mapSectors;
        }

        // 서버로부터 받은 지도 목록 저장
        public void save_maplist()
        {
            Console.WriteLine("JSON 저장 시작");
            List<object> list_json = new List<object>();
            List<object> list_path = new List<object>();

            Console.WriteLine("저장할 데이터 - ");
            foreach (MapSector map in this.MapSectors)
            {
                Console.WriteLine($"지도 번호 : {map.Num_Map} - 지도 이름 : {map.Name_Map} - 인덱스 : {map.Idx} - 경로 : {map.Path} - 경로(로컬원본) : {map.Path_Origin}");
            }


            foreach (MapSector mapData in this.MapSectors)
            {
                Dictionary<string, object> dict_meta = new Dictionary<string, object>();
                dict_meta.Add("NUM_MAP", mapData.Num_Map);
                dict_meta.Add("IDX", mapData.Idx);
                dict_meta.Add("NAME", mapData.Name_Map);

                list_json.Add(dict_meta);

                // 경로 정보 저장
                Dictionary<string, object> dict_path = new Dictionary<string, object>();

                dict_path.Add("NUM_MAP", mapData.Num_Map);
                dict_path.Add("PATH", mapData.Path);
                string fullPathForSize = mapData.Path;
                long file_size = File.Exists(fullPathForSize) ? new FileInfo(fullPathForSize).Length : 0;
                dict_path.Add("SIZE", file_size);

                list_path.Add(dict_path);
            }

            try
            {
                string json = JsonConvert.SerializeObject(list_json, Formatting.Indented); // 가독성을 위해 Indented 옵션 추가
                File.WriteAllText(this.path_map_meta, json);
                Console.WriteLine($"  -> 메타 JSON 파일 저장 성공: {this.path_map_meta}");

                string path_json = JsonConvert.SerializeObject(list_path, Formatting.Indented);
                File.WriteAllText(this.path_map_path, path_json);
                Console.WriteLine($"  -> 경로 JSON 파일 저장 성공: {this.path_map_path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  -> 메타 JSON 파일 저장 실패: {this.path_map_meta} - {ex.Message}");

            }
        }

       

        public void Compare_Maplist(ObservableCollection<MapSector> src, ObservableCollection<MapSector> tgt)
        {
            this.Map_Modified.Clear();
            this.Map_Removed.Clear();

            MapSectorComparer _comparer = new MapSectorComparer();
            HashSet<MapSector> mapRecvHashSet = new HashSet<MapSector>(tgt, _comparer);

            foreach (MapSector map in src)
            {
                MapSector correspondingMapInRecv = mapRecvHashSet.FirstOrDefault(p => _comparer.Equals(p, map));

                if (correspondingMapInRecv == null)
                {
                    this.Map_Removed.Add(map.Num_Map);
                }
                else
                {
                    if (map.Equals(correspondingMapInRecv))
                    {
                        this.Map_Modified.Add(correspondingMapInRecv);
                    }
                }
            }
        }
        public void Add_Maplist(ObservableCollection<MapSector> src, ObservableCollection<MapSector> tgt, bool filecopy = true)
        {
            Console.WriteLine("지도 동기화 시작 - 추가");

            for (int i = 0; i < tgt.Count; i++)
            {
                //Console.WriteLine($"현재 항목 : {item.Name} ({item.Path})");
                // 캡쳐 기준 해당 항목이 없는 경우 또는 캡쳐 기준 갯수가 하나도 없는 경우 추가
                if (!src.Contains(tgt[i]))
                {
                    src.Add(tgt[i]);
                    this.Map_Add.Add(tgt[i].Copy());          // 추가된 사항에 대해서 리스트에 추가
                    Console.WriteLine($"추가 핀 번호 : {tgt[i].Num_Map}");
                    if (filecopy)
                    {
                        string targetFilePath = "";
                        if (tgt[i].Path_Origin != "") targetFilePath = System.IO.Path.Combine(this.path_maps, System.IO.Path.GetFileName(tgt[i].Path_Origin));
                        if (File.Exists(tgt[i].Path_Origin))
                        {
                            Console.WriteLine($"원본 경로 : {tgt[i].Path_Origin}");
                            File.Copy(tgt[i].Path_Origin, targetFilePath, true);
                            src[i].Path = targetFilePath; // 복사한 파일의 경로를 업데이트
                        }
                        Console.WriteLine($"  추가 변동사항 발생, 항목 추가 및 파일 복사 시도 : {tgt[i].Name_Map} ({targetFilePath})");
                    }
                }
            }

            // 원본에서 num_map이 0인 항목은 전부 삭제 필요
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i].Num_Map == 0)
                {
                    src.RemoveAt(i);
                }
            }
        }
    }
    public class MapSector : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

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

        private string? _name_map;
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

        public string? Path { get; set; }

        public int SizeB { get; set; }

        public string? Path_Origin { get; set; }
        public ICommand? UpCommand { get; set; }
        public ICommand? DownCommand { get; set; }

        // 체크박스
        private bool _isSelected;
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
            return Num_Map == other.Num_Map;
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(Num_Map);
        }

        public MapSector Copy()
        {
            MapSector copy = new();

            copy.Name_Map = this.Name_Map;
            copy.Num_Map = this.Num_Map;
            copy.SizeB = this.SizeB;
            copy.Path = this.Path;
            copy.Path_Origin = this.Path_Origin;
            copy.Idx = this.Idx;
            copy.IsSelected = this.IsSelected;

            return copy;
        }

        // 깊은 복사, UpCommand와 DownCommand를 새로 생성하여 복사본에 할당
        public MapSector Copy(Action<object> onUpCommandExecuted, Action<object> onDownCommandExecuted)
        {
            MapSector copy = new();

            copy.Name_Map = this.Name_Map;
            copy.Num_Map = this.Num_Map;
            copy.SizeB = this.SizeB;
            copy.Path = this.Path;
            copy.Path_Origin = this.Path_Origin;
            copy.Idx = this.Idx;
            copy.IsSelected = this.IsSelected;

            copy.UpCommand = new RelayCommand(onUpCommandExecuted);
            copy.DownCommand = new RelayCommand(onDownCommandExecuted);

            return copy;
        }
    }

    public class MapSectorComparer : IEqualityComparer<MapSector>
    {
        public bool Equals(MapSector x, MapSector y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;

            return x.Num_Map == y.Num_Map;
        }

        public int GetHashCode(MapSector obj)
        {
            if (obj == null) return 0;
            return obj.Num_Map.GetHashCode();
        }

        // MapSector의 모든 속성 "내용"을 비교하는 메서드 (ClientPin처럼 Equals 오버라이딩이 안 된다면 필요)
        // 만약 MapSector의 Equals가 봉인되어 있다면 이 메서드를 사용하세요.
        public bool AreContentsEqual(MapSector x, MapSector y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;

            // MapSector.Equals에서 비교하는 모든 속성들을 여기에 추가
            
            return x.Num_Map == y.Num_Map &&
                x.Name_Map == y.Name_Map &&
                x.Idx == y.Idx; // Idx만으로 고유성 판단
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
    
        
    
