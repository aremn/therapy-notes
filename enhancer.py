import argparse
import json
import os
import re
import sys
import time
from typing import Any

try:
    import google.generativeai as genai

    HAS_GEMINI = True
except ImportError:
    genai = None
    HAS_GEMINI = False

try:
    if hasattr(sys.stdin, "reconfigure"):
        sys.stdin.reconfigure(encoding="utf-8")
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

VERBOSE = os.environ.get("ENHANCER_VERBOSE", "0") == "1"


API_KEY = os.environ.get("GEMINI_API_KEY")
MODEL_NAME = "gemini-2.5-flash-lite"


def dbg(msg: str) -> None:
    if VERBOSE:
        try:
            sys.stderr.write(f"[enhancer] {msg}\n")
            sys.stderr.flush()
        except Exception:
            pass


def setup_gemini() -> Any | None:
    dbg("=== GEMINI SETUP DEBUG ===")
    dbg(f"HAS_GEMINI: {HAS_GEMINI}")

    if not HAS_GEMINI:
        dbg("ERROR: google-generativeai package not installed")
        return None

    dbg(f"API_KEY present: {bool(API_KEY)}")
    if API_KEY:
        dbg(f"API_KEY length: {len(API_KEY)}")
        dbg(f"API_KEY starts with: {API_KEY[:10]}...")
    else:
        dbg("ERROR: No API key available")
        return None

    try:
        dbg("Configuring genai with API key...")
        genai.configure(api_key=API_KEY)
        dbg("Creating GenerativeModel...")
        model = genai.GenerativeModel(MODEL_NAME)
        dbg(f"SUCCESS: Initialized Gemini model: {MODEL_NAME}")
        return model
    except Exception as e:
        dbg(f"ERROR: Failed to initialize Gemini: {type(e).__name__}: {e}")
        import traceback

        dbg(f"Full traceback: {traceback.format_exc()}")
        return None


def create_enhancement_prompt(text: str) -> str:
    return f"""Ты - эксперт по улучшению текста. Твоя задача - улучшить качество предоставленного текста, сохранив при этом ВСЕ анонимизирующие теги в точности как они есть.

ВАЖНЫЕ ПРАВИЛА:
1. ОБЯЗАТЕЛЬНО сохрани все анонимизирующие теги ([PERSON_1], [ORG_1], [ADDRESS_1], [PHONE_1], [EMAIL_1], [DATE_1], [URL_1], [IP_1], [CARD_1], и т.д.) ТОЧНО как они написаны
2. Улучши грамматику, пунктуацию и стиль
3. Исправь опечатки и ошибки
4. Сделай текст более читаемым
5. Сохрани оригинальный смысл и структуру диалога
6. Не добавляй новую информацию, только улучшай существующую
7. Сохрани формат с номерами реплик [0], [1] и т.д.
8. Сохрани роли участников (therapist, client и т.д.)

ПРИМЕР:
Исходный текст:
[0] therapist: привет [PERSON_1] как дела
[1] client: нормально я работаю в [ORG_1]

Улучшенный текст:
[0] therapist: Привет, [PERSON_1], как дела?
[1] client: Нормально. Я работаю в [ORG_1].

ТЕКСТ ДЛЯ УЛУЧШЕНИЯ:
{text}

УЛУЧШЕННЫЙ ТЕКСТ:"""


def enhance_text_with_gemini(model: Any, text: str) -> str | None:
    dbg("=== ENHANCE_TEXT_WITH_GEMINI DEBUG ===")

    if not HAS_GEMINI or not model:
        dbg("ERROR: Gemini not available or model is None")
        return None

    try:
        dbg("Creating enhancement prompt...")
        prompt = create_enhancement_prompt(text)
        dbg(f"Prompt created. Length: {len(prompt)} chars")
        dbg(f"Input text length: {len(text)} chars")
        dbg(f"Input text preview: {text[:100]}...")

        dbg("Sending request to Gemini...")
        dbg(f"Model: {MODEL_NAME}")

        config = genai.types.GenerationConfig(
            temperature=0.1, max_output_tokens=70000, top_p=0.8, top_k=40
        )
        dbg(f"Generation config: temp={config.temperature}, max_tokens={config.max_output_tokens}")

        response = model.generate_content(prompt, generation_config=config)

        dbg("Response received from Gemini")
        dbg(f"Response object type: {type(response)}")

        try:
            if hasattr(response, "to_dict"):
                dump = json.dumps(response.to_dict(), ensure_ascii=False)
                dbg(f"Response.to_dict preview: {dump[:1200]}")
        except Exception:
            pass

        try:
            usage = getattr(response, "usage_metadata", None)
            if usage:
                in_tok = getattr(usage, "input_token_count", None) or getattr(
                    usage, "prompt_token_count", None
                )
                out_tok = getattr(usage, "output_token_count", None)
                total_tok = getattr(usage, "total_token_count", None)
                dbg(
                    f"Usage: input_tokens={in_tok} output_tokens={out_tok} total_tokens={total_tok}"
                )
        except Exception:
            pass

        try:
            pf = getattr(response, "prompt_feedback", None) or getattr(
                response, "promptFeedback", None
            )
            if pf:
                dbg(f"Prompt feedback: {pf}")
        except Exception:
            pass

        try:
            cands = getattr(response, "candidates", None)
            if cands:
                dbg(f"Candidates count: {len(cands)}")
                for i, cand in enumerate(cands):
                    fr = getattr(cand, "finish_reason", None) or getattr(cand, "finishReason", None)
                    dbg(f"candidate[{i}].finish_reason={fr}")
                    safety = getattr(cand, "safety_ratings", None) or getattr(
                        cand, "safetyRatings", None
                    )
                    if safety:
                        try:
                            for sr in safety:
                                cat = getattr(sr, "category", None) or getattr(
                                    sr, "harmCategory", None
                                )
                                prob = getattr(sr, "probability", None) or getattr(
                                    sr, "probability_score", None
                                )
                                blocked = getattr(sr, "blocked", None)
                                dbg(
                                    f"  safety: category={cat} blocked={blocked} probability={prob}"
                                )
                        except Exception:
                            dbg(f"  safety(raw): {safety}")
        except Exception:
            pass

        if hasattr(response, "text") and response.text:
            enhanced_text = response.text.strip()
            dbg(f"SUCCESS: Received response text (length: {len(enhanced_text)} chars)")
            dbg(f"Response preview: {enhanced_text[:100]}...")
            return enhanced_text
        else:
            dbg("ERROR: No text in response")
            dbg(f"Response attributes: {dir(response)}")
            if hasattr(response, "candidates"):
                dbg(f"Response candidates: {response.candidates}")
            if hasattr(response, "prompt_feedback"):
                dbg(f"Prompt feedback: {response.prompt_feedback}")
            return None

    except Exception as e:
        dbg(f"ERROR calling Gemini API: {type(e).__name__}: {e}")
        try:
            payload = getattr(e, "args", None)
            if payload:
                dbg(f"Exception args: {payload}")
            resp = getattr(e, "response", None)
            if resp:
                dbg(f"Exception.response: {resp}")
                try:
                    dbg(f"Exception.response dir: {dir(resp)}")
                except Exception:
                    pass
        except Exception:
            pass
        import traceback

        dbg(f"Full traceback: {traceback.format_exc()}")
        return None


def validate_anonymization_tags(original: str, enhanced: str) -> bool:
    try:
        tag_pattern = r"\[(?:PERSON|ORG|ADDRESS|PHONE|EMAIL|DATE|URL|IP|CARD|NAME)_\d+\]"

        original_tags = set(re.findall(tag_pattern, original, re.IGNORECASE))
        enhanced_tags = set(re.findall(tag_pattern, enhanced, re.IGNORECASE))

        if original_tags != enhanced_tags:
            dbg(f"Tag mismatch! Original: {original_tags}, Enhanced: {enhanced_tags}")
            missing_tags = original_tags - enhanced_tags
            extra_tags = enhanced_tags - original_tags
            if missing_tags:
                dbg(f"Missing tags: {missing_tags}")
            if extra_tags:
                dbg(f"Extra tags: {extra_tags}")
            return False

        dbg(f"All {len(original_tags)} anonymization tags preserved")
        return True

    except Exception as e:
        dbg(f"Error validating tags: {e}")
        return False


def fallback_enhancement(text: str) -> str:
    dbg("Using fallback enhancement")

    lines = text.split("\n")
    enhanced_lines = []

    for line in lines:
        line = line.strip()
        if not line:
            continue

        if line and not line.endswith((".", "!", "?", ":", ";")):
            line += "."

        if "] " in line:
            parts = line.split("] ", 1)
            if len(parts) == 2:
                speaker_part = parts[0] + "] "
                text_part = parts[1]
                if text_part and text_part[0].islower():
                    text_part = text_part[0].upper() + text_part[1:]
                line = speaker_part + text_part

        enhanced_lines.append(line)

    return "\n".join(enhanced_lines)


def main():
    parser = argparse.ArgumentParser(
        description="Enhance text quality while preserving anonymization tags"
    )
    parser.add_argument(
        "input_file",
        nargs="?",
        help="Input file path (if not provided, reads from stdin)",
    )
    parser.add_argument(
        "--output", "-o", help="Output file path (if not provided, writes to stdout)"
    )
    parser.add_argument("--verbose", "-v", action="store_true", help="Enable verbose logging")
    parser.add_argument(
        "--fallback",
        action="store_true",
        help="Use fallback enhancement instead of Gemini",
    )

    args = parser.parse_args()

    global VERBOSE
    if args.verbose:
        VERBOSE = True

    try:
        dbg(f"Enhancer starting (PID: {os.getpid()})")
        dbg(f"Python version: {sys.version.splitlines()[0]}")
        dbg(f"Working directory: {os.getcwd()}")
        dbg(f"Verbose mode: {VERBOSE}")
        dbg(f"Fallback mode: {args.fallback}")
        dbg(f"HAS_GEMINI: {HAS_GEMINI}")
        dbg(f"API_KEY configured: {bool(API_KEY)}")

        if not API_KEY:
            dbg("Set GEMINI_API_KEY to enable enhancement.")

    except Exception:
        pass

    start_time = time.time()

    try:
        if args.input_file:
            dbg(f"Reading input from file: {args.input_file}")
            with open(args.input_file, encoding="utf-8") as f:
                input_text = f.read()
        else:
            dbg("Reading input from stdin")
            input_text = sys.stdin.read()
    except Exception as e:
        dbg(f"Failed to read input: {e}")
        return 1

    if input_text and input_text[0] == "\ufeff":
        input_text = input_text.lstrip("\ufeff")

    if not input_text.strip():
        dbg("Empty input text")
        return 0

    try:
        input_preview = input_text[:200].replace("\n", " ").replace("\r", " ")
        dbg(f'Input length: {len(input_text)} chars, preview: "{input_preview}"')
    except Exception:
        pass

    enhanced_text = None

    if not args.fallback:
        model = setup_gemini()
        if model:
            enhanced_text = enhance_text_with_gemini(model, input_text)

            if enhanced_text and not validate_anonymization_tags(input_text, enhanced_text):
                dbg("Anonymization tag validation failed, falling back to original text")
                enhanced_text = None

    if enhanced_text is None:
        if args.fallback:
            enhanced_text = fallback_enhancement(input_text)
        else:
            dbg("Enhancement failed, returning original text")
            enhanced_text = input_text

    try:
        output_preview = enhanced_text[:200].replace("\n", " ").replace("\r", " ")
        elapsed_ms = int((time.time() - start_time) * 1000)
        dbg(f'Output length: {len(enhanced_text)} chars, preview: "{output_preview}"')
        dbg(f"Processing time: {elapsed_ms}ms")
    except Exception:
        pass

    try:
        if args.output:
            dbg(f"Writing output to file: {args.output}")
            with open(args.output, "w", encoding="utf-8") as f:
                f.write(enhanced_text)
        else:
            sys.stdout.write(enhanced_text)
    except Exception as e:
        dbg(f"Failed to write output: {e}")
        return 1

    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        dbg("Interrupted by user")
        sys.exit(1)
    except Exception as e:
        try:
            sys.stderr.write(f"enhancer error: {e}\n")
        except Exception:
            pass
        sys.exit(1)
