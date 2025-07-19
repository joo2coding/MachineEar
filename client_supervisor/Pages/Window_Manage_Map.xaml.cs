using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace client_supervisor
{
    /// <summary>
    /// Window_Manage_Map.xaml에 대한 상호 작용 논리
    /// </summary>
    /// 
    public partial class Window_Manage_Map : Window
    {
        public ObservableCollection<MapSector> MapSectors { get; set; }
        public ObservableCollection<MapSector> Capture_MapSectors { get; set; }     // 캡쳐, 이걸로 수정, 적용을 누르면 이 값을 덮어씌움
        public Window_Manage_Map(ObservableCollection<MapSector> mapSectors)        
        {
            InitializeComponent();
            this.MapSectors = mapSectors;

            this.Capture_MapSectors = new ObservableCollection<MapSector>(
                mapSectors.Select(m => new MapSector
                {
                    Idx = m.Idx,
                    Name = m.Name,
                    Path = m.Path,
                    SizeB = m.SizeB,
                    Path_Origin = m.Path_Origin,
                    UpCommand = new RelayCommand(OnUpCommandExecuted),
                    DownCommand = new RelayCommand(OnDownCommandExecuted),
                    IsSelected = false
                })
            );
            this.DataContext = this;
        }

        // 지도 경로 확인 및 행 추가
        private void pb_add_Click(object sender, RoutedEventArgs e)
        {
            // 지도 추가 창 생성
            Window_Add_Map add_Map = new Window_Add_Map();
            if (add_Map.ShowDialog() == true)
            {
                string path_file = System.IO.Path.GetFileName(add_Map.textbox_path.Text);

                // 데이터그리드에 행 추가
                int newIdx = Capture_MapSectors.Count > 0 ? Capture_MapSectors[Capture_MapSectors.Count - 1].Idx + 1 : 1;
                MapSector newSector = new MapSector
                {
                    Idx = newIdx,
                    Name = add_Map.textbox_name.Text,
                    Path_Origin = add_Map.textbox_path.Text,
                    Path = path_file,
                    UpCommand = new RelayCommand(OnUpCommandExecuted),
                    DownCommand = new RelayCommand(OnDownCommandExecuted),
                    IsSelected = false
                };
                Capture_MapSectors.Add(newSector);
            }
        }

        // 외부에서 이벤트 연결
        public void conn_event()
        {
            foreach (var sector in this.MapSectors)
            {
                sector.UpCommand = new RelayCommand(OnUpCommandExecuted);
                sector.DownCommand = new RelayCommand(OnDownCommandExecuted);
            }
        }

        // 인덱스 번호를 각 순번에 맞게 업데이트
        private void UpdateIndices()
        {
            for (int i = 0; i < Capture_MapSectors.Count; i++)
            {
                // UI 업데이트를 위해 Idx 속성의 setter를 통해 값 변경
                Capture_MapSectors[i].Idx = i + 1;
            }
        }

        // 순서에서 위쪽 화살표 클릭 시 실행
        private void OnDownCommandExecuted(object parameter) // object로 받음
        {
            if (parameter is MapSector sectorToMove)
            {
                int oldIndex = Capture_MapSectors.IndexOf(sectorToMove);
                if (oldIndex < Capture_MapSectors.Count - 1)
                {
                    Capture_MapSectors.Move(oldIndex, oldIndex + 1);
                    UpdateIndices();
                }
            }
        }

        // 순서에서 아래쪽 화살표 클릭 시 실행
        private void OnUpCommandExecuted(object parameter)
        {
            if (parameter is MapSector sectorToMove)
            {
                int oldIndex = Capture_MapSectors.IndexOf(sectorToMove);
                if (oldIndex > 0)
                {
                    Capture_MapSectors.Move(oldIndex, oldIndex - 1);
                    UpdateIndices();
                }
            }
        }

        private void pb_remove_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = Capture_MapSectors.Where(s => s.IsSelected).ToList();

            if (selectedItems.Count == 0)
            {
                return;
            }

            foreach(var item in selectedItems)
            {
                Capture_MapSectors.Remove(item);
            }
            UpdateIndices();
        }

        private void pb_adapt_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("적용하시겠습니까?", "지도 목록 적용", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.OK)
            {
                this.MapSectors = this.Capture_MapSectors; // 캡쳐된 값을 원본 MapSectors에 덮어씌움
                this.DialogResult = true;
            }
        }
    }
}
