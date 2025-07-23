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
                    Num_Map = m.Num_Map,
                    Idx = m.Idx,
                    Name_Map = m.Name_Map,
                    Path = m.Path,
                    Path_Origin = m.Path_Origin,
                    SizeB = m.SizeB,
                    //UpCommand = new RelayCommand(OnUpCommandExecuted),
                    //DownCommand = new RelayCommand(OnDownCommandExecuted),
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
                // 데이터그리드에 행 추가
                int newIdx = Capture_MapSectors.Count > 0 ? Capture_MapSectors[Capture_MapSectors.Count - 1].Idx + 1 : 1;
                MapSector newSector = new MapSector
                {
                    Idx = newIdx,
                    Name_Map = add_Map.textbox_name.Text,
                    Path_Origin = add_Map.textbox_path.Text,
                    //UpCommand = new RelayCommand(OnUpCommandExecuted),
                    //DownCommand = new RelayCommand(OnDownCommandExecuted),
                    IsSelected = false
                };
                long file_size = File.Exists(newSector.Path) ? new FileInfo(newSector.Path).Length : 0;
                newSector.SizeB = (int)file_size;

                Capture_MapSectors.Add(newSector);
            }
        }

        // 외부에서 이벤트 연결
        //public void conn_event()
        //{
        //    foreach (var sector in this.MapSectors)
        //    {
        //        sector.UpCommand = new RelayCommand(OnUpCommandExecuted);
        //        sector.DownCommand = new RelayCommand(OnDownCommandExecuted);
        //    }
        //}

        // 인덱스 번호를 각 순번에 맞게 업데이트
        //private void UpdateIndices()
        //{
        //    for (int i = 0; i < Capture_MapSectors.Count; i++)
        //    {
        //        // UI 업데이트를 위해 Idx 속성의 setter를 통해 값 변경
        //        Capture_MapSectors[i].Idx = i + 1;
        //    }
        //}

        //// 순서에서 위쪽 화살표 클릭 시 실행
        //private void OnDownCommandExecuted(object parameter) // object로 받음
        //{
        //    if (parameter is MapSector sectorToMove)
        //    {
        //        int oldIndex = Capture_MapSectors.IndexOf(sectorToMove);
        //        if (oldIndex < Capture_MapSectors.Count - 1)
        //        {
        //            Capture_MapSectors.Move(oldIndex, oldIndex + 1);
        //            UpdateIndices();
        //        }
        //    }
        //}

        //// 순서에서 아래쪽 화살표 클릭 시 실행
        //private void OnUpCommandExecuted(object parameter)
        //{
        //    if (parameter is MapSector sectorToMove)
        //    {
        //        int oldIndex = Capture_MapSectors.IndexOf(sectorToMove);
        //        if (oldIndex > 0)
        //        {
        //            Capture_MapSectors.Move(oldIndex, oldIndex - 1);
        //            UpdateIndices();
        //        }
        //    }
        //}

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
            //UpdateIndices();
        }

        private void pb_adapt_Click(object sender, RoutedEventArgs e)
        {
            // 사용자에게 적용 여부 확인
            MessageBoxResult confirmResult = MessageBox.Show("적용하시겠습니까?", "지도 목록 적용", MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (confirmResult == MessageBoxResult.OK)
            {
                // 1. Idx 중복 검사
                HashSet<int> seenIdx = new HashSet<int>();
                List<int> duplicateIdxs = new List<int>();

                foreach (var item in this.Capture_MapSectors)
                {
                    if (!seenIdx.Add(item.Idx))
                    {
                        if (!duplicateIdxs.Contains(item.Idx))
                        {
                            duplicateIdxs.Add(item.Idx);
                        }
                    }
                }

                // 2. Idx 순차성 및 시작 값 (1) 검사
                // Idx 값들을 오름차순으로 정렬하여 리스트로 만듭니다.
                List<int> sortedIdxs = this.Capture_MapSectors.Select(m => m.Idx).OrderBy(idx => idx).ToList();

                bool isSequentialAndStartsAtOne = true;
                string sequentialCheckMessage = "";

                if (!sortedIdxs.Any() || sortedIdxs[0] != 1)
                {
                    isSequentialAndStartsAtOne = false;
                    sequentialCheckMessage = "Idx 값이 1부터 시작하지 않거나 목록이 비어있습니다.\n";
                }
                else
                {
                    for (int i = 0; i < sortedIdxs.Count; i++)
                    {
                        // 현재 Idx 값이 예상되는 순차적인 값과 다른지 확인 (예: 1, 2, 4 -> 3이 빠짐)
                        if (sortedIdxs[i] != i + 1)
                        {
                            isSequentialAndStartsAtOne = false;
                            sequentialCheckMessage = $"Idx 값이 순차적이지 않습니다. {i + 1}번 맵의 Idx가 {sortedIdxs[i]} 입니다. (예상: {i + 1})\n";
                            break; // 첫 번째 비순차적 지점에서 중단
                        }
                    }
                }

                // 모든 검사 결과를 바탕으로 최종 판단
                if (duplicateIdxs.Any())
                {
                    string message = "다음 Idx 값들이 중복됩니다:\n";
                    foreach (int idx in duplicateIdxs)
                    {
                        message += $"- {idx}\n";
                    }
                    MessageBox.Show(message, "인덱스 중복 감지", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (!isSequentialAndStartsAtOne)
                {
                    // 순차적이지 않거나 1부터 시작하지 않는 경우
                    MessageBox.Show(sequentialCheckMessage, "인덱스 순차성 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    // 모든 검사를 통과했을 때만 적용
                    this.MapSectors = this.Capture_MapSectors; // 캡쳐된 값을 원본 MapSectors에 덮어씌움
                    this.DialogResult = true; // 대화상자의 결과를 true로 설정 (필요시)
                    MessageBox.Show("지도 목록이 성공적으로 적용되었습니다.", "적용 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}
