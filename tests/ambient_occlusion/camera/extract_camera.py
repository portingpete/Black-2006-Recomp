"""Extract authored AO camera code into the build tree without guest game code."""
from __future__ import annotations

import argparse
from pathlib import Path
import re


def function(text: str, signature: str) -> str:
    matches = list(re.finditer(re.escape(signature) + r"\s*\{", text))
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one definition of {signature}")
    start = matches[0].start()
    brace = text.index("{", start)
    depth = 0
    # Ignore comment/literal braces, so extraction follows the real C body.
    tokens = re.compile(r'/\*.*?\*/|//[^\n]*|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[{}]', re.S)
    for token in tokens.finditer(text, brace):
        value = token.group()
        if value == "{":
            depth += 1
        elif value == "}":
            depth -= 1
            if depth == 0:
                return text[start:token.end()]
    raise ValueError(f"Unterminated definition of {signature}")


def derive(text: str) -> str:
    start_marker = "/* BEGIN BLACK_AO_CAMERA"
    end_marker = "/* END BLACK_AO_CAMERA */"
    if text.count(start_marker) != 1 or text.count(end_marker) != 1:
        raise ValueError("Expected exactly one authored AO camera block")
    begin = text.index(start_marker)
    end = text.index(end_marker, begin) + len(end_marker)
    camera = text[begin:end]
    helper = function(camera, "static void black_ao_camera_publish(uint32_t camera)")
    wrapper = function(text, "void sub_000D2F40(void)")
    compact = re.sub(r"\s+", "", wrapper)
    producer = "sub_000D2F40_gen();"
    restore = "if(adjusted)GUEST32(camera+0x70u)=saved_window;"
    publisher = "black_ao_camera_publish(camera);"
    if compact.count(producer) != 1 or compact.count(publisher) != 1:
        raise ValueError("The camera wrapper must call its original producer and publisher exactly once")
    if restore not in compact or not compact.index(producer) < compact.index(restore) < compact.index(publisher):
        raise ValueError("Publish the actual completed primary camera after restoring the temporary FOV window")
    guard = function(text, "static int black_pc_ram_span(uint32_t address, uint32_t bytes)")
    return "/* Derived from the current authored source; no generated guest code. */\n" + guard + "\n" + helper + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    derived = derive(args.source.read_text(encoding="utf-8-sig"))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(derived, encoding="utf-8")


if __name__ == "__main__":
    main()
