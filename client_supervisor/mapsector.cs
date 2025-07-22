using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace client_supervisor
{
    public partial class MainWindow
    {
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

            // map의 경로파일이 존재하는지 확인
            if (!File.Exists(this.path_map_path))
            {
                File.WriteAllText(this.path_map_path, "[]");     // 기본 객체 생성 후 파일 저장
            }

            string read_string = File.ReadAllText(this.path_map_meta);
            ObservableCollection<MapSector> mapSectors = new ObservableCollection<MapSector>();
            mapSectors = JsonConvert.DeserializeObject<ObservableCollection<MapSector>>(read_string);     // 문자열을 json으로 변환

            string read_path = File.ReadAllText(this.path_map_path);
            JArray pathObject = JsonConvert.DeserializeObject<JArray>(read_path);
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

            Window_Manage_Map manage_Map = new Window_Manage_Map(mapSectors);       // 화면 출력을 하지 않음
            manage_Map.conn_event();        // 파일에서 불러온 목록들에 대하여 이벤트 연결

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
            List<object> list_json = new List<object>();
            List<object> list_path = new List<object>();

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

        // 지도 목록 비교 - 실행 후 추가
        public List<MapSector> compare_maplist_add(ObservableCollection<MapSector> src, ObservableCollection<MapSector> tgt, bool filecopy = true)
        {
            Console.WriteLine("지도 동기화 시작 - 추가");
            List<MapSector> list_add = new();

            for ( int i = 0; i < tgt.Count; i ++)
            {
                //Console.WriteLine($"현재 항목 : {item.Name} ({item.Path})");
                // 캡쳐 기준 해당 항목이 없는 경우 또는 캡쳐 기준 갯수가 하나도 없는 경우 추가
                if (!src.Contains(tgt[i]))
                {
                    list_add.Add(tgt[i].Copy());          // 추가된 사항에 대해서 리스트에 추가
                    Console.WriteLine($"추가 핀 번호 : {tgt[i].Idx}");
                    if (filecopy)
                    {
                        string targetFilePath = System.IO.Path.Combine(this.path_maps, System.IO.Path.GetFileName(tgt[i].Path_Origin));
                        if (File.Exists(tgt[i].Path_Origin))
                        {
                            Console.WriteLine($"원본 경로 : {tgt[i].Path_Origin}");
                            File.Copy(tgt[i].Path_Origin, targetFilePath, true);
                            tgt[i].Path = targetFilePath; // 복사한 파일의 경로를 업데이트
                        }
                        Console.WriteLine($"  추가 변동사항 발생, 항목 추가 및 파일 복사 시도 : {tgt[i].Name_Map} ({targetFilePath})");
                    }
                }
            }
            return list_add;
        }

        // 지도 목록 비교 - 실행 후 삭제
        public List<int> compare_maplist_delete(ObservableCollection<MapSector> src, ObservableCollection<MapSector> tgt, bool filedelete = true)
        {
            Console.WriteLine("지도 동기화 시작 - 삭제");
            List<int> list_remove = new();
            baseImage.Source = null;
            System.Threading.Thread.Sleep(50);

            foreach (var item in src)
            {
                //Console.WriteLine($"  현재 항목 : {item.Name} ({item.Path})");
                // 캡쳐 기준 해당 항목이 없는 경우 삭제
                if (!tgt.Contains(item))
                {
                    list_remove.Add(item.Num_Map);      // 삭제할 지도 번호를 목록에 추가
                    Console.WriteLine($"삭제 핀 번호 : {item.Idx}");

                    if (filedelete)
                    {
                        string fullPathToDelete = System.IO.Path.Combine(this.path_maps, item.Path);
                        File.Delete(fullPathToDelete);

                        Console.WriteLine($"  삭제 변동사항 발생, 파일 삭제 시도 : {item.Name_Map} ({fullPathToDelete})");
                    }
                }
            }

            return list_remove;
        }
    }
}
    
        
    
