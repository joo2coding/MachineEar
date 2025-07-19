using System;
using System.Collections;
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
    public class ClientPin_Map : ClientPin
    {
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
    }

    /// <summary>
    /// Window_Manage_Map.xaml에 대한 상호 작용 논리
    /// </summary>
    /// 
    public partial class Window_Manage_Pin : Window
    {
        private ObservableCollection<MapSector> MapSectors { get; set; }
        public ObservableCollection<ClientPin> PinList_Origin { get; set; }
        public ObservableCollection<ClientPin_Map> Capture_PinList { get; set; }
        public Window_Manage_Pin(ObservableCollection<ClientPin> pinList, ObservableCollection<MapSector> mapSectors)        
        {
            InitializeComponent();

            this.MapSectors = mapSectors;       // 지도 목록 받기
            this.PinList_Origin = pinList;

            this.Capture_PinList = new ObservableCollection<ClientPin_Map>(
                pinList.Select(m => new ClientPin_Map
                {
                    Idx = m.Idx,
                    Name = m.Name,
                    MapIndex = m.MapIndex,
                    Name_Location = m.Name_Location,
                    PosX = m.PosX,
                    PosY = m.PosY,
                    MAC = m.MAC,
                    Date_Reg = m.Date_Reg,
                    State_Anomaly = m.State_Anomaly,
                    Name_Manager = m.Name_Manager,
                    Mode_Color = m.Mode_Color,
                    IsSelected = false,
                    Name_Map = MapSectors.FirstOrDefault(sector => sector.Idx == m.MapIndex)?.Name
                })
            );

            this.DataContext = this;            
        }

        private void pb_remove_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = Capture_PinList.Where(s => s.IsSelected).ToList();

            if (selectedItems.Count == 0)
            {
                return;
            }

            foreach(var item in selectedItems)
            {
                Capture_PinList.Remove(item);
            }
        }

        private void pb_adapt_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("적용하시겠습니까?", "핀 목록 적용", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.OK)
            {
                ObservableCollection<ClientPin> Modified = new ObservableCollection<ClientPin>(
                    this.Capture_PinList.Select(m => new ClientPin
                    {
                        Idx = m.Idx,
                        Name = m.Name,
                        MapIndex = m.MapIndex,
                        Name_Location = m.Name_Location,
                        PosX = m.PosX,
                        PosY = m.PosY,
                        MAC = m.MAC,
                        Date_Reg = m.Date_Reg,
                        State_Anomaly = m.State_Anomaly,
                        Name_Manager = m.Name_Manager,
                        Mode_Color = m.Mode_Color,
                        IsSelected = false
                    })
                );
                this.PinList_Origin = Modified;

                this.DialogResult = true;
            }
        }
    }
}
