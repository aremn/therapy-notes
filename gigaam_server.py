import json
import os
import sys
import traceback
import warnings

import torch
import torchaudio
from transformers import AutoModel, AutoProcessor

script_dir = os.path.dirname(os.path.abspath(__file__))
hf_cache_dir = os.path.join(script_dir, "hf_cache")
os.environ["HF_HOME"] = hf_cache_dir
os.environ["TRANSFORMERS_CACHE"] = hf_cache_dir

try:
    if hasattr(sys.stdin, "reconfigure"):
        sys.stdin.reconfigure(encoding="utf-8")
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

try:
    warnings.filterwarnings(
        "ignore", message=r"Parallel batch decoding is not currently supported.*"
    )
except Exception:
    pass


class _FilteredStderr:
    _SUPPRESS_SUBSTRINGS = [
        "Parallel batch decoding is not currently supported in this platform. Falling back to sequential decoding.",
    ]

    def __init__(self, real):
        self._real = real

    def write(self, s):
        try:
            for sub in self._SUPPRESS_SUBSTRINGS:
                if sub in s:
                    return 0
            return self._real.write(s)
        except Exception:
            return 0

    def flush(self):
        try:
            return self._real.flush()
        except Exception:
            pass

    def isatty(self):
        try:
            return getattr(self._real, "isatty", lambda: False)()
        except Exception:
            return False

    def fileno(self):
        try:
            return getattr(self._real, "fileno", lambda: -1)()
        except Exception:
            return -1


try:
    sys.stderr = _FilteredStderr(sys.stderr)
except Exception:
    pass

MODEL_ID = "waveletdeboshir/gigaam-ctc-with-lm"
MODEL_STRIDE_MS = 40
CHUNK_SEC = 30.0
CHUNK_OVERLAP_SEC = 0.5
DECODE_BEAM_WIDTH = 16
FAST_THRESHOLD_SEC = 15 * 60
FAST_CHUNK_SEC = 20.0
FAST_OVERLAP_SEC = 0.2


def log_err(msg: str):
    try:
        sys.stderr.write(msg + "\n")
        sys.stderr.flush()
    except Exception:
        pass


def load_model():
    device = torch.device("cpu")
    os.environ.setdefault("CUDA_VISIBLE_DEVICES", "")

    log_err("Loading processor...")
    processor = AutoProcessor.from_pretrained(MODEL_ID, trust_remote_code=True)

    log_err("Loading model...")
    try:
        model = AutoModel.from_pretrained(
            MODEL_ID, trust_remote_code=True, torch_dtype=torch.float32
        )
        model.eval()
        if next(model.parameters()).device.type != "cpu":
            model = model.cpu()
    except NotImplementedError as e:
        if "meta tensor" in str(e):
            log_err("Meta tensor detected, using to_empty() method...")
            model = AutoModel.from_pretrained(
                MODEL_ID, trust_remote_code=True, torch_dtype=torch.float32
            )
            model.eval()
            model = model.to_empty(device=device)
        else:
            raise

    log_err(f"Model loaded on device: {next(model.parameters()).device}")
    return device, processor, model


def transcribe_one(device, processor, model, audio_path: str, rid: str = None, progress_cb=None):
    wav, sr = torchaudio.load(audio_path)
    if sr != 16000:
        wav = torchaudio.functional.resample(wav, sr, 16000)
    if wav.dim() == 2 and wav.size(0) > 1:
        wav_mono = wav[0]
    else:
        wav_mono = wav.squeeze(0)

    total_samples = wav_mono.shape[-1]
    sr = 16000
    total_sec = total_samples / float(sr)
    fast_mode = total_sec >= FAST_THRESHOLD_SEC
    chunk_len = FAST_CHUNK_SEC if fast_mode else CHUNK_SEC
    overlap_len = FAST_OVERLAP_SEC if fast_mode else CHUNK_OVERLAP_SEC

    chunk_samples = int(chunk_len * sr)
    overlap_samples = int(overlap_len * sr)
    hop = max(1, chunk_samples - overlap_samples)

    all_words = []
    texts = []
    last_end_abs = 0.0

    with torch.no_grad():
        chunk_idx = 0
        total_chunks = max(1, (total_samples + hop - 1) // hop)
        for start in range(0, total_samples, hop):
            end = min(total_samples, start + chunk_samples)
            chunk = wav_mono[start:end]
            if chunk.numel() == 0:
                continue
            inputs = processor(chunk, sampling_rate=sr, return_tensors="pt")
            inputs = {k: v.to(device) if hasattr(v, "to") else v for k, v in inputs.items()}
            out = model(**inputs)
            logits = (
                out.logits
                if hasattr(out, "logits")
                else (out[0] if isinstance(out, (list, tuple)) else out)
            )

            if fast_mode:
                outputs = processor.batch_decode(
                    logits=logits.detach().cpu().numpy(),
                    beam_width=1,
                    alpha=0.0,
                    beta=0.0,
                    output_word_offsets=False,
                )
                chunk_offset_sec = start / float(sr)
                txt = outputs.text[0]
                if txt:
                    txt = txt.strip()
                    texts.append(txt)
                    all_words.append(
                        {
                            "word": txt,
                            "start": round(chunk_offset_sec, 2),
                            "end": round(chunk_offset_sec + (len(chunk) / float(sr)), 2),
                        }
                    )
                if progress_cb is not None:
                    try:
                        progress_cb(
                            {
                                "id": rid,
                                "event": "progress",
                                "chunk_index": chunk_idx,
                                "total_chunks": total_chunks,
                                "start_sec": round(chunk_offset_sec, 2),
                                "end_sec": round(chunk_offset_sec + (len(chunk) / float(sr)), 2),
                                "text": txt or "",
                                "fast_mode": True,
                            }
                        )
                    except Exception:
                        pass
                del logits, outputs, inputs
            else:
                outputs = processor.batch_decode(
                    logits=logits.detach().cpu().numpy(),
                    beam_width=DECODE_BEAM_WIDTH,
                    alpha=0.5,
                    beta=0.5,
                    output_word_offsets=True,
                )
                chunk_offset_sec = start / float(sr)
                for d in outputs.word_offsets[0]:
                    s = d["start_offset"] * MODEL_STRIDE_MS / 1000.0 + chunk_offset_sec
                    e = d["end_offset"] * MODEL_STRIDE_MS / 1000.0 + chunk_offset_sec
                    if s < last_end_abs - 0.05:
                        continue
                    all_words.append(
                        {
                            "word": d["word"],
                            "start": round(s, 2),
                            "end": round(e, 2),
                        }
                    )
                    last_end_abs = max(last_end_abs, e)

                txt = outputs.text[0]
                if txt:
                    texts.append(txt.strip())
                if progress_cb is not None:
                    try:
                        progress_cb(
                            {
                                "id": rid,
                                "event": "progress",
                                "chunk_index": chunk_idx,
                                "total_chunks": total_chunks,
                                "start_sec": round(chunk_offset_sec, 2),
                                "end_sec": round(
                                    last_end_abs
                                    if txt
                                    else (chunk_offset_sec + (len(chunk) / float(sr))),
                                    2,
                                ),
                                "text": txt or "",
                                "fast_mode": False,
                            }
                        )
                    except Exception:
                        pass
                del logits, outputs, inputs
            chunk_idx += 1

    full_text = " ".join(t for t in texts if t).strip()
    return {
        "success": True,
        "transcription": full_text,
        "word_timestamps": all_words,
    }


def main():
    single_mode = len(sys.argv) >= 3 and sys.argv[1] == "--single"
    if single_mode:
        audio_path = sys.argv[2]
        try:
            device, processor, model = load_model()
            result = transcribe_one(device, processor, model, audio_path)
            print(json.dumps(result, ensure_ascii=False))
            sys.stdout.flush()
            return
        except Exception as e:
            log_err(f"FATAL: transcription failed: {e}")
            log_err(traceback.format_exc())
            print(
                json.dumps(
                    {"success": False, "error": f"transcription_failed: {e}"},
                    ensure_ascii=False,
                )
            )
            sys.stdout.flush()
            sys.exit(1)

    try:
        device, processor, model = load_model()
    except Exception as e:
        log_err(f"FATAL: failed to load model: {e}")
        log_err(traceback.format_exc())
        print(
            json.dumps(
                {"id": None, "success": False, "error": f"load_failed: {e}"},
                ensure_ascii=False,
            )
        )
        sys.stdout.flush()
        return

    try:
        print(json.dumps({"id": "_ready_", "success": True, "event": "ready"}, ensure_ascii=False))
        sys.stdout.flush()
    except Exception:
        pass

    while True:
        line = sys.stdin.readline()
        if not line:
            break
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
            rid = req.get("id")
            cmd = req.get("cmd")
            if cmd == "transcribe":
                path = req.get("path")
                if not path or not os.path.exists(path):
                    resp = {"id": rid, "success": False, "error": "file_not_found"}
                else:
                    try:

                        def _progress(evt):
                            try:
                                print(json.dumps(evt, ensure_ascii=False))
                                sys.stdout.flush()
                            except Exception:
                                pass

                        result = transcribe_one(
                            device,
                            processor,
                            model,
                            path,
                            rid=rid,
                            progress_cb=_progress,
                        )
                        resp = {"id": rid, "success": True, "result": result}
                    except Exception as e:
                        log_err(f"transcribe error: {e}")
                        log_err(traceback.format_exc())
                        resp = {"id": rid, "success": False, "error": str(e)}
            elif cmd == "ping":
                resp = {"id": rid, "success": True, "result": "pong"}
            else:
                resp = {"id": rid, "success": False, "error": "unknown_cmd"}
        except Exception as e:
            resp = {"id": None, "success": False, "error": f"bad_request: {e}"}
        print(json.dumps(resp, ensure_ascii=False))
        sys.stdout.flush()


if __name__ == "__main__":
    main()
