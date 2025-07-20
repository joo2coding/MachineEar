using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Input;

namespace client_supervisor
{
    public partial class MainWindow
    {
        Dictionary<string, Action<WorkItem>> dict_proc_recv = new Dictionary<string, Action<WorkItem>>();
        Dictionary<string, Func<WorkItem, WorkItem>> dict_proc_send = new Dictionary<string, Func<WorkItem, WorkItem>>();

        /// <summary>
        /// 수신 프로토콜에 대해서 딕셔너리에 추가
        /// </summary>
        public void AddRecvProtocol()
        {
            // 수신측 메서드 추가
            this.dict_proc_recv.Add("1-0-0", Recv_Req_Conn);
            this.dict_proc_recv.Add("1-0-1", Recv_Req_Code_Error);
            this.dict_proc_recv.Add("1-0-2", Recv_Req_Code_Anomaly);
            this.dict_proc_recv.Add("1-1-0", Recv_Req_Mic_List);
            this.dict_proc_recv.Add("1-2-2", Recv_Req_Event_List);
            this.dict_proc_recv.Add("1-3-0", Recv_Req_Map_List);
            this.dict_proc_recv.Add("1-3-3", Recv_Req_Map_Data);

            // 송신측 메서드 추가
            this.dict_proc_send.Add("1-0-0", Send_Req_Conn);
            this.dict_proc_send.Add("1-0-1", Send_Req_Code_Error);

        }

        public WorkItem ExcuteCommand_Send(WorkItem item)
        {
            WorkItem send_item = new();

            if (this.dict_proc_recv.ContainsKey(item.Protocol))
            {
                // 프로토콜과 같은 키에 해당하는 메서드 실행
                send_item = this.dict_proc_send[item.Protocol](item);
            }
            else
            {
                Console.WriteLine($"오류: 알 수 없는 프로토콜 '{item.Protocol}' 입니다.");
            }

            return send_item;
        }

        /// <summary>
        /// 서버로부터 수신하여 프로토콜에 맞는 메서드 실행
        /// </summary>
        public void ExcuteCommand_Recv(WorkItem item)
        {
            Console.Write($"[Recv] Protocol : {item.Protocol}");
            if(item.BinaryData.Length > 0)
            {
                Console.WriteLine($" - Binary Data Size : {item.BinaryData.Length}");
            }
            else
            {
                Console.WriteLine();
            }

            if (this.dict_proc_recv.ContainsKey(item.Protocol))
            {
                // 프로토콜과 같은 키에 해당하는 메서드 실행
                this.dict_proc_recv[item.Protocol](item);
            }
            else
            {
                Console.WriteLine($"오류: 알 수 없는 프로토콜 '{item.Protocol}' 입니다.");
            }
        }

        // 송신측 코드
        // 1-0-0 : 접속 요청
        private WorkItem Send_Req_Conn(WorkItem item)        
        {
            WorkItem send_item = item;

            return send_item;
        }
        // 1-0-1 : 고장 원인 목록 요청
        private WorkItem Send_Req_Code_Error(WorkItem item)
        {
            WorkItem send_item = item;

            return send_item;
        }
        // 1-0-2 : 이상 상태 처리 목록 요청
        private WorkItem Send_Req_Code_Anomaly(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-1-0 : 클라이언트(마이크) 목록 요청
        private WorkItem Send_Req_Mic_List(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-1-1 : 클라이언트(마이크) 가동 전송
        private WorkItem Send_Mic_State_Change(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-1-2 : 클라이언트(마이크) 가동 전송, 전체를 대상
        private WorkItem Send_Mic_State_ChangeAll(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-2-1 : 이상 상황이 발생한 핀에 대해서 세부사항 작성 후 서버에 전송
        private WorkItem Send_Mic_State_Anomaly(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-2-2 : 특정 요청일에 대하여 전체 기록 요청
        private WorkItem Send_Req_Event_List(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-2-3 : 이전 기록에 대한 정보 수정 전송
        private WorkItem Send_Data_Event_List(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-3-0 : 지도 목록 요청
        private WorkItem Send_Req_Map_List(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-3-1 : 지도 목록 수정
        private WorkItem Send_Data_Map_List(WorkItem item)
        {
            WorkItem send_item= item;
            return send_item;
        }

        // 수신측 코드
        // 1-0-0 : 접속 요청 결과 수신
        private void Recv_Req_Conn(WorkItem item)
        {
            // 서버에서 접속 거부 당했을 경우
            if (item.JsonData["RESPONSE"].ToString() == "NO")
            {
                // 연결 종료
                this.clientService.Dispose();
            }
        }
        // 1-0-1 : 고장 원인 목록 요청
        private void Recv_Req_Code_Error(WorkItem item)
        {

        }
        // 1-0-2 : 이상 상태 처리 목록 요청
        private void Recv_Req_Code_Anomaly(WorkItem item)
        {

        }
        // 1-1-0 : 클라이언트(마이크) 목록 요청, 핀 리스트
        private void Recv_Req_Mic_List(WorkItem item)
        {

        }
        // 1-2-0 : 이상상황 발생한 핀에 대하여 데이터 수신
        private void Recv_Mic_State_Anomaly(WorkItem item)
        {

        }
        // 1-2-2 : 특정 요청일에 대하여 전체 기록 수신
        private void Recv_Req_Event_List(WorkItem item)
        {

        }
        // 1-3-0 : 지도 목록 수신
        private void Recv_Req_Map_List(WorkItem item)
        {

        }
        // 1-3-3 : 요청한 지도 번호에 따른 파일 수신
        private void Recv_Req_Map_Data(WorkItem item)
        {

        }
    }
}
