#!/usr/bin/env python3
# Offline EN→X worker for DialogueAr. No network after model is on disk.
# Protocol: one JSON object per stdin line.
#   {"cmd":"ping"} -> {"ok":true,"pong":true}
#   {"cmd":"tr","text":"..."} -> {"ok":true,"t":"..."}  ("ar" kept as alias)
# On successful model load, emits one {"ok":true,"pong":true} ready line.
# argv[1] = model dir, argv[2] = optional Opus target token (e.g. >>cmn_Hans<<).

import json
import re
import sys
import os

# Marian opus-mt max_position_embeddings = 512. Leave headroom for special tokens.
MAX_TOKENS = 480
MAX_NEW_CAP = 256  # dialogue lines rarely need more; smaller = faster generate


def split_sentences(text: str):
    parts = re.split(r"(?<=[.!?…])\s+", text.strip())
    return [p for p in parts if p.strip()]


def pack_chunks(sentences, tok):
    """Pack sentences into chunks that stay under MAX_TOKENS."""
    chunks = []
    buf = []
    buf_ids = 0
    for sent in sentences:
        n = len(tok.encode(sent, add_special_tokens=False))
        if n > MAX_TOKENS:
            if buf:
                chunks.append(" ".join(buf))
                buf, buf_ids = [], 0
            words = sent.split()
            wbuf = []
            wids = 0
            for w in words:
                wn = len(tok.encode(w, add_special_tokens=False)) + 1
                if wbuf and wids + wn > MAX_TOKENS:
                    chunks.append(" ".join(wbuf))
                    wbuf, wids = [], 0
                wbuf.append(w)
                wids += wn
            if wbuf:
                chunks.append(" ".join(wbuf))
            continue

        if buf and buf_ids + n + 1 > MAX_TOKENS:
            chunks.append(" ".join(buf))
            buf, buf_ids = [], 0
        buf.append(sent)
        buf_ids += n + 1

    if buf:
        chunks.append(" ".join(buf))
    return chunks or [""]


def translate_one(tok, model, text: str, fast: bool = True) -> str:
    import torch

    inputs = tok(text, return_tensors="pt", truncation=True, max_length=512)
    in_len = int(inputs["input_ids"].shape[-1])
    max_new = max(48, min(MAX_NEW_CAP, in_len * 2 + 16))
    beams = 1 if fast else 2
    with torch.inference_mode():
        out = model.generate(
            **inputs,
            max_new_tokens=max_new,
            num_beams=beams,
            do_sample=False,
            use_cache=True,
        )
    return tok.decode(out[0], skip_special_tokens=True)


def translate_full(tok, model, text: str, fast: bool = True) -> str:
    ids = tok.encode(text, add_special_tokens=True)
    if len(ids) <= MAX_TOKENS:
        return translate_one(tok, model, text, fast)

    sentences = split_sentences(text)
    if len(sentences) <= 1:
        sentences = [text]

    parts = []
    for chunk in pack_chunks(sentences, tok):
        if not chunk.strip():
            continue
        parts.append(translate_one(tok, model, chunk, fast))
    return " ".join(parts)


def main():
    model_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "opus-mt-en-ar")
    target_token = sys.argv[2].strip() if len(sys.argv) > 2 else ""
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stdin.reconfigure(encoding="utf-8")

    try:
        from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
        import torch
    except Exception as e:
        print(json.dumps({"ok": False, "error": f"deps: {e}"}), flush=True)
        return

    # Prefer throughput on CPU for dialogue latency.
    try:
        torch.set_num_threads(max(1, min(8, os.cpu_count() or 4)))
        torch.set_num_interop_threads(1)
    except Exception:
        pass

    try:
        tok = AutoTokenizer.from_pretrained(model_dir, local_files_only=True)
        model = AutoModelForSeq2SeqLM.from_pretrained(model_dir, local_files_only=True)
        model.eval()
        # Warm kernels so the first real line is not the cold compile path.
        _ = translate_one(tok, model, "Hello.")
    except Exception as e:
        print(json.dumps({"ok": False, "error": f"model: {e}"}), flush=True)
        return

    print(json.dumps({"ok": True, "pong": True}), flush=True)

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except Exception as e:
            print(json.dumps({"ok": False, "error": str(e)}), flush=True)
            continue

        cmd = req.get("cmd")
        if cmd == "ping":
            print(json.dumps({"ok": True, "pong": True}), flush=True)
            continue

        if cmd == "tr":
            text = req.get("text") or ""
            fast = bool(req.get("fast", True))
            if not text.strip():
                print(json.dumps({"ok": True, "t": text, "ar": text}), flush=True)
                continue
            try:
                src = f"{target_token} {text}".strip() if target_token else text
                translated = translate_full(tok, model, src, fast)
                print(json.dumps({"ok": True, "t": translated, "ar": translated}, ensure_ascii=False), flush=True)
            except Exception as e:
                print(json.dumps({"ok": False, "error": str(e), "t": text, "ar": text}), flush=True)
            continue

        print(json.dumps({"ok": False, "error": "unknown cmd"}), flush=True)


if __name__ == "__main__":
    main()
