import os
import sys
import time
import json
import base64
import struct
import socket
import argparse
import subprocess
import tempfile
import urllib.request
import imageio_ffmpeg

CHROME_PATHS = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    os.path.expandvars(r"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]

def find_browser():
    for p in CHROME_PATHS:
        if os.path.isfile(p):
            return p
    raise FileNotFoundError("No se encontró Google Chrome ni Microsoft Edge en las rutas estándar.")

def find_free_port():
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(('127.0.0.1', 0))
    port = s.getsockname()[1]
    s.close()
    return port

class CDPClient:
    def __init__(self, ws_url):
        parts = ws_url[5:].split('/', 1)
        host, port_str = parts[0].split(':')
        path = '/' + parts[1]
        
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.connect((host, int(port_str)))
        
        key = base64.b64encode(os.urandom(16)).decode()
        req = (
            f"GET {path} HTTP/1.1\r\n"
            f"Host: {host}:{port_str}\r\n"
            "Upgrade: websocket\r\n"
            "Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            "Sec-WebSocket-Version: 13\r\n\r\n"
        )
        self.sock.sendall(req.encode())
        res = self.sock.recv(4096)
        if b"101" not in res:
            raise ConnectionError(f"Fallo en handshake WebSocket: {res.decode('utf-8', errors='ignore')}")
        self.req_id = 0

    def send(self, method, params=None):
        self.req_id += 1
        payload = json.dumps({"id": self.req_id, "method": method, "params": params or {}})
        data = payload.encode('utf-8')
        length = len(data)
        mask = os.urandom(4)
        header = bytearray([0x81])
        if length <= 125:
            header.append(0x80 | length)
        elif length <= 65535:
            header.append(0x80 | 126)
            header.extend(struct.pack('>H', length))
        else:
            header.append(0x80 | 127)
            header.extend(struct.pack('>Q', length))
        header.extend(mask)
        masked = bytearray(b ^ mask[i % 4] for i, b in enumerate(data))
        self.sock.sendall(header + masked)
        return self.req_id

    def recv(self, expected_id=None):
        def read_exact(n):
            buf = bytearray()
            while len(buf) < n:
                chunk = self.sock.recv(n - len(buf))
                if not chunk:
                    raise EOFError("Socket cerrado prematuramente")
                buf.extend(chunk)
            return buf

        while True:
            msg_payload = bytearray()
            while True:
                hdr = read_exact(2)
                fin = bool(hdr[0] & 0x80)
                opcode = hdr[0] & 0x0F
                masked = bool(hdr[1] & 0x80)
                plen = hdr[1] & 0x7F
                if plen == 126:
                    plen = struct.unpack('>H', read_exact(2))[0]
                elif plen == 127:
                    plen = struct.unpack('>Q', read_exact(8))[0]
                
                if masked:
                    mkey = read_exact(4)
                    raw = read_exact(plen)
                    chunk = bytes(b ^ mkey[i % 4] for i, b in enumerate(raw))
                else:
                    chunk = bytes(read_exact(plen))
                
                msg_payload.extend(chunk)
                if fin:
                    break
            
            txt = msg_payload.decode('utf-8', errors='ignore')
            msg = json.loads(txt)
            if expected_id is None or msg.get("id") == expected_id:
                return msg

    def evaluate(self, expression):
        req_id = self.send("Runtime.evaluate", {"expression": expression})
        return self.recv(req_id)

    def capture_frame(self, quality=95):
        req_id = self.send("Page.captureScreenshot", {"format": "jpeg", "quality": quality})
        res = self.recv(req_id)
        b64_data = res.get("result", {}).get("data", "")
        return base64.b64decode(b64_data)

    def close(self):
        try:
            self.sock.close()
        except Exception:
            pass

def render(output_file, duration=50.0, fps=60, width=1920, height=1080):
    script_dir = os.path.dirname(os.path.abspath(__file__))
    html_path = os.path.join(script_dir, "index.html")
    html_url = f"file:///{html_path.replace(os.sep, '/')}?clean=1"
    
    browser_exe = find_browser()
    ffmpeg_exe = imageio_ffmpeg.get_ffmpeg_exe()
    port = find_free_port()

    # Ensure UTF-8 output on Windows consoles
    if sys.platform == "win32":
        try:
            sys.stdout.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass

    print("=" * 65)
    print("[PROMO] TaskbarMusicWidget - Renderizador Directo 1080p 60fps")
    print("=" * 65)
    print(f"- Navegador: {browser_exe}")
    print(f"- Motor FFmpeg: {ffmpeg_exe}")
    print(f"- Resolucion: {width}x{height} @ {fps} fps")
    print(f"- Duracion: {duration:.1f} segundos (Total fotogramas: {int(duration * fps)})")
    print(f"- Destino: {output_file}")
    print("=" * 65)

    # Launch Chrome headless with CDP in an isolated user profile
    user_dir = tempfile.mkdtemp(prefix="taskbar_promo_chrome_")
    chrome_cmd = [
        browser_exe,
        "--headless=new",
        "--disable-gpu",
        "--no-sandbox",
        f"--user-data-dir={user_dir}",
        f"--window-size={width},{height}",
        "--hide-scrollbars",
        "--force-device-scale-factor=1",
        f"--remote-debugging-port={port}",
        html_url
    ]

    print("\n[1/4] Iniciando navegador en modo headless...")
    proc = subprocess.Popen(chrome_cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    cdp = None
    ffmpeg_proc = None

    try:
        # Wait for debugging port
        ws_url = None
        for _ in range(30):
            time.sleep(0.3)
            try:
                resp = urllib.request.urlopen(f"http://127.0.0.1:{port}/json")
                tabs = json.loads(resp.read().decode())
                pages = [t for t in tabs if t.get("type") == "page"]
                if pages:
                    ws_url = pages[0]["webSocketDebuggerUrl"]
                    break
            except Exception:
                pass

        if not ws_url:
            raise TimeoutError("No se pudo conectar a Chrome DevTools Protocol.")

        print(f"[2/4] Conexión CDP establecida: {ws_url}")
        cdp = CDPClient(ws_url)

        # Force exact resolution override
        cdp.send("Emulation.setDeviceMetricsOverride", {
            "width": width,
            "height": height,
            "deviceScaleFactor": 1,
            "mobile": False
        })

        # Allow DOM and assets to settle
        time.sleep(1.0)
        cdp.evaluate("evaluateFrame(0.0)")
        time.sleep(0.5)

        # Setup FFmpeg pipe
        print("[3/4] Inicializando codificador H.264 MP4...")
        ffmpeg_cmd = [
            ffmpeg_exe,
            "-y",
            "-f", "image2pipe",
            "-framerate", str(fps),
            "-i", "-",
            "-vf", f"scale={width}:{height}",
            "-c:v", "libx264",
            "-preset", "faster",
            "-crf", "18",
            "-pix_fmt", "yuv420p",
            output_file
        ]

        ffmpeg_proc = subprocess.Popen(ffmpeg_cmd, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

        total_frames = int(duration * fps)
        print(f"[4/4] Renderizando {total_frames} fotogramas a calidad cinematográfica...\n")

        start_time = time.time()
        for frame_idx in range(total_frames):
            t = frame_idx / fps
            cdp.evaluate(f"evaluateFrame({t:.4f})")
            frame_bytes = cdp.capture_frame(quality=95)
            ffmpeg_proc.stdin.write(frame_bytes)

            # Progress feedback every 30 frames (0.5s of video)
            if frame_idx % 30 == 0 or frame_idx == total_frames - 1:
                elapsed = time.time() - start_time
                done_pct = (frame_idx + 1) / total_frames * 100
                speed = (frame_idx + 1) / max(elapsed, 0.001)
                eta = (total_frames - (frame_idx + 1)) / max(speed, 0.001)
                bar = "#" * int(done_pct / 4) + "-" * (25 - int(done_pct / 4))
                sys.stdout.write(
                    f"\r[{bar}] {done_pct:5.1f}% | Fotograma {frame_idx + 1}/{total_frames} ({t:.1f}s) | "
                    f"{speed:4.1f} fps | ETA: {int(eta // 60):02d}:{int(eta % 60):02d} "
                )
                sys.stdout.flush()

        print("\n\nFinalizando codificacion del archivo MP4...")
        ffmpeg_proc.stdin.close()
        ffmpeg_proc.wait()

        total_time = time.time() - start_time
        file_size_mb = os.path.getsize(output_file) / (1024 * 1024)

        print("\n" + "=" * 65)
        print("[OK] VIDEO GENERADO CON EXITO SIN GRABACION DE PANTALLA!")
        print("=" * 65)
        print(f"- Archivo: {output_file}")
        print(f"- Tamano: {file_size_mb:.2f} MB")
        print(f"- Resolucion: {width}x{height} (1080p)")
        print(f"- Tasa de refresco: {fps} FPS (60.0 fps nativos)")
        print(f"- Tiempo de render: {int(total_time // 60)}m {int(total_time % 60)}s")
        print("- Sin controles, sin marcas ni telemetria: 100% puro.")
        print("=" * 65)

    finally:
        if cdp:
            cdp.close()
        if ffmpeg_proc and ffmpeg_proc.poll() is None:
            try:
                ffmpeg_proc.terminate()
            except Exception:
                pass
        if proc and proc.poll() is None:
            try:
                proc.terminate()
            except Exception:
                pass

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Renderiza el vídeo promocional de TaskbarMusicWidget a 1080p 60fps sin grabar pantalla.")
    parser.add_argument("--duration", type=float, default=50.0, help="Duración en segundos (por defecto 50.0)")
    parser.add_argument("--fps", type=int, default=60, help="Fotogramas por segundo (por defecto 60)")
    parser.add_argument("--out", type=str, default="", help="Ruta del archivo MP4 de salida")
    args = parser.parse_args()

    default_out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "TaskbarMusicWidget_Cinematic_1080p60.mp4")
    out_path = args.out if args.out else default_out
    render(out_path, duration=args.duration, fps=args.fps)
