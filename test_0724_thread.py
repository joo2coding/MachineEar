import socket
import json
import struct
import torch
import numpy as np
import librosa
import io
import time  # 시간 측정용
import queue # FIFO
import threading # thread 라이브러리
from model import Wavegram_AttentionMap

audio_queue = queue.Queue() # 비어있는 큐 자료구조 --> 초기화

# 모델 로드 (전역)
ckpt_path = 'MACHINE_EAR_MODEL.ckpt'
model = Wavegram_AttentionMap.load_from_checkpoint(ckpt_path, h=128, lr=0.001)
model.eval()
device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
model = model.to(device)

CLASS_LABELS = [
    "fan normal", "fan abnormal",
    "pump normal", "pump abnormal",
    "slider normal", "slider abnormal",
    "valve normal", "valve abnormal",
    "gearbox normal", "gearbox abnormal",
    "ToyConveyor normal", "ToyConveyor abnormal"
]
CLASS = ['fan', 'pump', 'slider', 'valve', 'gearbox', 'ToyConveyor']

# 서버에게 데이터 송신하는 함수
def send_data(sock, json_):
    try:
        json_str = json.dumps(json_)
        print(f'송신 : {json_str}')
        json_bytes = json_str.encode('utf-8') # json의 바이트 배열
        total_bytes = len(json_bytes) # 송신한 데이터의 총 크기 (json 만 보낼 경우)

        # 4바이트로 길이 인코딩 (네트워크 바이트 오더, big-endian)
        header_total = struct.pack('>I', total_bytes)
        header_json = struct.pack('>I', len(json_bytes))
        sock.sendall(header_total + header_json + json_bytes)
    except Exception as e:
        print('전송 중 에러:', e)

# 서버로부터 모든 데이터가 수신되었는지 확인하는 함수
def chk_receive_data(sock, bytes_):
    buf = b''  # 바이너리로 읽음
    while len(buf) < bytes_:
        chunk = sock.recv(bytes_ - len(buf))
        if not chunk:
            raise ConnectionError('서버와 연결이 끊어졌습니다!')
        buf += chunk
    return buf

# 서버에게 데이터 수신하는 함수
def receive_data(sock):
    try:
        # header 8바이트 수신하기
        header = chk_receive_data(sock, 8) # 앞에서 8바이트 읽어서 저장 ---> header(4+4)
        total_len, json_len = struct.unpack('>II', header) # 4바이트씩 뜯어서 ---> 전체 길이 / json 길이 저장

        # header 뒤 실제 데이터 수신하기
        body = chk_receive_data(sock, total_len)
        # json과 data 각각 저장하기
        json_ = body[:json_len].decode("utf-8")
        if total_len > json_len: # 데이터가 있는 경우
            data_ = body[json_len:total_len]
        else: # 데이터가 없는 경우
            data_ = None
        return json_, data_
    except Exception as e:
        print('수신 에러: ', e)
        return None, None

# 수신 쓰레드: 오디오 데이터와 메타데이터를 audio_queue에 put
def receiver_thread(sock):
    while True:
        json_, data_ = receive_data(sock)
        if json_ is None or data_ is None:
            continue
        json_obj = json.loads(json_)
        protocol = json_obj.get('PROTOCOL')
        if protocol == '2-1-0':
            # 메타데이터 추출 (필요에 따라 수정)
            meta = json_obj.get('__META__', {})
            NUM_PIN = json_obj.get('NUM_PIN', 0)
            SR = meta.get('SAMPLING_RATE', 16000)
            TIME = meta.get('TIME', 10)
            FILE_PATH = json_obj.get('FILE_PATH', '')
            # 튜플로 큐에 삽입
            audio_queue.put((data_, SR, TIME, NUM_PIN, FILE_PATH))
            print(f"[수신] 오디오 큐 사이즈: {audio_queue.qsize()}")
            # 서버에 OK 응답
            # send_data(sock, {'RESPONSE': 'OK'})

# 예측 & 송신 쓰레드: audio_queue에서 꺼내 바로 예측 후 결과 전송
def predictor_sender_thread(sock, model):
    while True:
        data_, SR, TIME, NUM_PIN, FILE_PATH = audio_queue.get()  # block until data available

        # --- 오디오 바이너리 메모리에서 직접 예측 ---
        buf = io.BytesIO(data_)
        y, _ = librosa.load(buf, sr=SR, duration=TIME)
        target_len = int(SR * TIME)
        if len(y) < target_len:
            y = np.pad(y, (0, target_len - len(y)))
        else:
            y = y[:target_len]
        x = torch.tensor(y, dtype=torch.float32).unsqueeze(0).to(device)
        # 더미 라벨 생성 (12-class 분류이므로, 0~11 중 아무거나. 보통 0)
        dummy_label = torch.tensor([0], dtype=torch.long).to(device)

        # --- 예측 시간 측정 시작---
        start = time.time()
        with torch.no_grad():
            logits_tuple = model(x, dummy_label)
            logits = logits_tuple[0] if isinstance(logits_tuple, tuple) else logits_tuple
            pred = torch.argmax(logits, dim=1).item()
            pred_str = CLASS_LABELS[pred]
        # --- 예측 시간 측정 끝 ---
        elapsed = time.time() - start
        print(f'[예측] 결과: {pred_str} (걸린 시간: {elapsed:.4f}초)')

        # 예측 결과 전송
        class_, result_ = pred_str.split()
        result_json = {
            'PROTOCOL': '2-2-0',
            'NUM_PIN': NUM_PIN,
            'CLASS': CLASS.index(class_)+1,
            'RESULT': result_.upper(),
            'FILE_PATH': FILE_PATH
        }
        send_data(sock, result_json)
        print(f'[송신] 예측 결과 전송: {result_json}')

def proc_2_0_0(sock, conn_switch):
    while conn_switch == 0:
        send_data(sock, {'PROTOCOL': '2-0-0'})
        json_, _ = receive_data(sock)
        json_obj = json.loads(json_)
        if json_obj['PROTOCOL'] == '2-0-0':
            if json_obj['RESPONSE'] == 'NO':
                print('서버한테 connect NO 받았어요')
                continue
            else:
                print('서버한테 connect OK 받았어요')
                conn_switch = 1
    return conn_switch

def main():
    IP = '10.10.20.111'
    PORT = 9000

    client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    client_socket.connect((IP, PORT))
    client_socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    print(f'서버 {IP}:{PORT}에 연결됨')

    # 접속 요청하기
    conn_switch = 0
    if conn_switch == 0:
        conn_switch = proc_2_0_0(client_socket, conn_switch)

    # 멀티쓰레드 시작
    # target : 쓰레드 동작 함수 / agrgs : 필요한 인자값 / daemon :
    t1 = threading.Thread(target=receiver_thread, args=(client_socket,), daemon=True)
    t2 = threading.Thread(target=predictor_sender_thread, args=(client_socket, model), daemon=True)
    t1.start() # 쓰레드 동작 시작
    t2.start()

    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print('\n사용자 중단')
    finally:
        client_socket.close()
        print('연결 종료')

if __name__ == "__main__":
    main()
