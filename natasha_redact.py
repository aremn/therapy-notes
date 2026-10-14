import json
import os
import re
import sys
import time
from collections import defaultdict
from typing import Any

try:
    import spacy
    from spacy.matcher import Matcher
except Exception:
    spacy = None
    Matcher = None

try:
    if hasattr(sys.stdin, "reconfigure"):
        sys.stdin.reconfigure(encoding="utf-8")
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

from natasha import (
    Doc,
    MorphVocab,
    NewsEmbedding,
    NewsMorphTagger,
    NewsNERTagger,
    NewsSyntaxParser,
    Segmenter,
)

try:
    import pymorphy3

    morph_analyzer = pymorphy3.MorphAnalyzer()
    HAS_PYMORPHY = True
except ImportError:
    HAS_PYMORPHY = False
    morph_analyzer = None

if spacy is not None:
    try:
        try:
            nlp = spacy.load("ru_core_news_lg")
        except OSError:
            try:
                nlp = spacy.load("ru_core_news_sm")
            except OSError:
                nlp = None
        HAS_SPACY = nlp is not None
    except Exception:
        nlp = None
        HAS_SPACY = False
else:
    nlp = None
    HAS_SPACY = False


VERBOSE = os.environ.get("NATASHA_VERBOSE", "0") == "1"
FALLBACK_PER_NAMES = os.environ.get("NATASHA_FALLBACK_PER_NAMES", "0") == "1"
CONFIG_DIR = os.environ.get("NATASHA_CONFIG_DIR")


def dbg(msg: str):
    if VERBOSE:
        try:
            sys.stderr.write(f"[natasha_redact] {msg}\n")
            sys.stderr.flush()
        except Exception:
            pass


def make_tag(kind: str, entity_id: str) -> str:
    return f"[{entity_id}]"


class FixedUltimateAnonymizer:
    def __init__(self):
        if HAS_PYMORPHY:
            self.morph = morph_analyzer
        else:
            self.morph = None

        if HAS_SPACY:
            self.nlp = nlp
            self.setup_compound_matcher()
        else:
            self.nlp = None
            self.matcher = None

        self.segmenter = Segmenter()
        self.morph_vocab = MorphVocab()
        self.emb = NewsEmbedding()
        self.morph_tagger = NewsMorphTagger(self.emb)
        self.syntax_parser = NewsSyntaxParser(self.emb)
        self.ner_tagger = NewsNERTagger(self.emb)

        self.load_dictionaries()

        self.counters = defaultdict(int)
        self.mapping = {}

    def setup_compound_matcher(self):
        if not self.nlp:
            self.matcher = None
            return

        self.matcher = Matcher(self.nlp.vocab)

        compound_pattern_dot = [
            {"TEXT": {"REGEX": r"^(Яндекс|Mail|VK|Сбер)$"}, "OP": "?"},
            {"TEXT": {"REGEX": r"^[\.\-]$"}},
            {"TEXT": {"REGEX": r"^(Дзен|ru|Group|Банк|Маркет)$"}, "OP": "?"},
        ]

        compound_pattern_org_loc = [
            {"ENT_TYPE": "ORG", "OP": "+"},
            {"LOWER": "на"},
            {"ENT_TYPE": {"IN": ["LOC", "GPE"]}, "OP": "+"},
        ]

        self.matcher.add("COMPOUND_ORG_DOT", [compound_pattern_dot])
        self.matcher.add("COMPOUND_ORG_LOC", [compound_pattern_org_loc])

    def load_dictionaries(self):
        try:
            dbg(f"CONFIG_DIR={CONFIG_DIR or 'None'}")
            if CONFIG_DIR and os.path.isdir(CONFIG_DIR):
                org_path = os.path.join(CONFIG_DIR, "organizations.json")
                loc_path = os.path.join(CONFIG_DIR, "locations.json")
                alias_path = os.path.join(CONFIG_DIR, "aliases.json")
            else:
                org_path = loc_path = alias_path = ""

            base_dir = os.path.dirname(os.path.abspath(__file__))
            if not (org_path and os.path.exists(org_path)):
                org_path = os.path.join(base_dir, "organizations.json")
            if not (loc_path and os.path.exists(loc_path)):
                loc_path = os.path.join(base_dir, "locations.json")
            if not (alias_path and os.path.exists(alias_path)):
                alias_path = os.path.join(base_dir, "aliases.json")

            stopwords_path = (
                os.path.join(CONFIG_DIR, "stopwords.json")
                if CONFIG_DIR and os.path.isdir(CONFIG_DIR)
                else ""
            )
            if not (stopwords_path and os.path.exists(stopwords_path)):
                stopwords_path = os.path.join(base_dir, "stopwords.json")

            if (
                not os.path.exists(org_path)
                or not os.path.exists(loc_path)
                or not os.path.exists(alias_path)
                or not os.path.exists(stopwords_path)
            ):
                cwd = os.getcwd()
                org_path2 = os.path.join(cwd, "Configuration", "organizations.json")
                loc_path2 = os.path.join(cwd, "Configuration", "locations.json")
                alias_path2 = os.path.join(cwd, "Configuration", "aliases.json")
                stopwords_path2 = os.path.join(cwd, "Configuration", "stopwords.json")
                dbg(
                    f"Trying CWD config: {org_path2}, {loc_path2}, {alias_path2}, {stopwords_path2}"
                )
                if (
                    os.path.exists(org_path2)
                    and os.path.exists(loc_path2)
                    and os.path.exists(alias_path2)
                    and os.path.exists(stopwords_path2)
                ):
                    org_path, loc_path, alias_path, stopwords_path = (
                        org_path2,
                        loc_path2,
                        alias_path2,
                        stopwords_path2,
                    )

            dbg(
                f"DICT PATHS -> org='{org_path}', loc='{loc_path}', alias='{alias_path}', stopwords='{stopwords_path}'"
            )

            with open(org_path, encoding="utf-8") as f:
                orgs = json.load(f)
                self.company_dict = {org.lower() for org in orgs}

            with open(loc_path, encoding="utf-8") as f:
                locs = json.load(f)
                self.location_dict = {loc.lower() for loc in locs}

            with open(alias_path, encoding="utf-8") as f:
                self.alias_map = json.load(f)

            with open(stopwords_path, encoding="utf-8") as f:
                stopwords_list = json.load(f)
                self.stop_words = {word.lower() for word in stopwords_list}

            dbg(
                f"Loaded dictionaries: {len(self.company_dict)} orgs, {len(self.location_dict)} locs, {len(self.alias_map)} aliases, {len(self.stop_words)} stopwords"
            )
        except Exception as e:
            dbg(f"Dictionary loading failed: {e}")
            self.company_dict = set()
            self.location_dict = set()
            self.alias_map = {}
            self.stop_words = set()

    def find_compound_entities(self, text: str) -> list[dict[str, Any]]:
        if not self.matcher:
            return []

        doc = self.nlp(text)
        compound_entities = []

        matches = self.matcher(doc)

        for _match_id, start, end in matches:
            span = doc[start:end]
            compound_entities.append(
                {
                    "text": span.text,
                    "start": span.start_char,
                    "end": span.end_char,
                    "label": "ORG",
                    "source": "compound_matcher",
                }
            )

        return compound_entities

    def get_entities_from_dictionary(self, text: str) -> list[dict[str, Any]]:
        entities = []
        text_lower = text.lower()

        compound_patterns = {
            r"\bяндекс\.дзен\b": "яндекс дзен",
            r"\bяндекс\s*дзен\b": "яндекс дзен",
            r"\bmail\.ru\s+group\b": "mail ru group",
            r"\bmail\s*ru\s+group\b": "mail ru group",
            r"\bсбер\s*маркет\b": "сбермаркет",
            r"\bгазпром\s*-?\s*медиа\b": "газпром медиа",
        }

        for pattern, _canonical_name in compound_patterns.items():
            for match in re.finditer(pattern, text, re.IGNORECASE):
                original_text = text[match.start() : match.end()]
                entities.append(
                    {
                        "text": original_text,
                        "start": match.start(),
                        "end": match.end(),
                        "label": "ORG",
                        "source": "dictionary_compound",
                    }
                )

        found_ranges = [(e["start"], e["end"]) for e in entities]

        for company in self.company_dict:
            pattern = r"\b" + re.escape(company) + r"[а-яё]*\b"
            for match in re.finditer(pattern, text_lower, re.IGNORECASE):
                if not any(
                    self._ranges_overlap(match.start(), match.end(), start, end)
                    for start, end in found_ranges
                ):
                    original_text = text[match.start() : match.end()]
                    entities.append(
                        {
                            "text": original_text,
                            "start": match.start(),
                            "end": match.end(),
                            "label": "ORG",
                            "source": "dictionary",
                        }
                    )

        for location in self.location_dict:
            pattern = r"\b" + re.escape(location) + r"[а-яё]*\b"
            for match in re.finditer(pattern, text_lower, re.IGNORECASE):
                if not any(
                    self._ranges_overlap(match.start(), match.end(), start, end)
                    for start, end in found_ranges
                ):
                    original_text = text[match.start() : match.end()]
                    entities.append(
                        {
                            "text": original_text,
                            "start": match.start(),
                            "end": match.end(),
                            "label": "LOC",
                            "source": "dictionary",
                        }
                    )

        return entities

    def _ranges_overlap(self, start1: int, end1: int, start2: int, end2: int) -> bool:
        return not (end1 <= start2 or end2 <= start1)

    def get_entities_from_regex(self, text: str) -> list[dict[str, Any]]:
        entities = []

        patterns = {
            "EMAIL": r"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
            "PHONE": r"\+?\d{1,3}?[-\s.]?\(?\d{3}\)?[-\s.]?\d{3}[-\s.]?\d{2}[-\s.]?\d{2}",
            "URL": r'https?://[^\s"\'<>]+',
            "DATE": r"\b\d{1,2}\s+(?:январ[яь]|феврал[яь]|марта?|апрел[яь]|ма[яй]|июн[яь]|июл[яь]|августа?|сентябр[яь]|октябр[яь]|ноябр[яь]|декабр[яь])\s+\d{4}\b",
            "ADDRESS": r"\b(?:улица|ул|проспект|пр-т|площадь|пл|переулок|пер|шоссе|ш)\.?\s+[а-яёА-ЯЁ\d\s-]+(?:,?(\s+дом|\s+д\.?))?\s+\d+[/\w-]*",
        }

        for label, pattern in patterns.items():
            for match in re.finditer(pattern, text, re.IGNORECASE):
                clean_text = match.group(0)
                if label == "URL":
                    clean_text = re.sub(r"[.,!?:;]$", "", clean_text)

                entities.append(
                    {
                        "text": clean_text.strip(),
                        "start": match.start(),
                        "end": match.start() + len(clean_text),
                        "label": label,
                        "source": "regex_matcher",
                    }
                )
        dbg(f"Regex matcher found {len(entities)} entities.")
        return entities

    def get_entities_from_natasha(self, text: str) -> list[dict[str, Any]]:
        dbg(f"Processing real text: '{text[:100]}...' (len={len(text)})")

        doc = Doc(text)
        doc.segment(self.segmenter)
        doc.tag_morph(self.morph_tagger)
        doc.parse_syntax(self.syntax_parser)
        doc.tag_ner(self.ner_tagger)

        dbg(f"Real text spans found: {len(doc.spans)}")

        entities = []

        for span in doc.spans:
            dbg(f"Raw span: '{span.text}' ({span.type}) at {span.start}-{span.stop}")
            if span.type in ["PER", "ORG", "LOC"]:
                entities.append(
                    {
                        "text": span.text,
                        "start": span.start,
                        "end": span.stop,
                        "label": span.type,
                        "source": "natasha",
                    }
                )
                dbg(f"Natasha found: '{span.text}' ({span.type}) at {span.start}-{span.stop}")

        return entities

    def get_entities_from_spacy(self, text: str) -> list[dict[str, Any]]:
        if not self.nlp:
            return []

        doc = self.nlp(text)
        entities = []

        for ent in doc.ents:
            if ent.label_ in ["PER", "PERSON", "ORG", "LOC", "GPE"]:
                label = self._normalize_label(ent.label_)
                entities.append(
                    {
                        "text": ent.text,
                        "start": ent.start_char,
                        "end": ent.end_char,
                        "label": label,
                        "source": "spacy",
                    }
                )
                dbg(
                    f"spaCy found: '{ent.text}' ({ent.label_} -> {label}) at {ent.start_char}-{ent.end_char}"
                )

        return entities

    def find_known_person_parts(
        self, text: str, existing_entities: list[dict[str, Any]]
    ) -> list[dict[str, Any]]:
        person_entities = [e for e in existing_entities if e["label"] == "PER"]
        if not person_entities:
            return []
        known_parts = {}
        for p_entity in person_entities:
            canonical_key = self.get_canonical_key(p_entity["text"], "PER")
            for part in canonical_key.split():
                if len(part) > 2:
                    if part not in known_parts:
                        known_parts[part] = canonical_key
        if not known_parts:
            return []

        dbg(f"Known person parts for second pass: {list(known_parts.keys())}")
        new_entities = []
        covered_ranges = [(e["start"], e["end"]) for e in existing_entities]
        for match in re.finditer(r"\b[а-яёА-ЯЁ-]+\b", text):
            word = match.group(0)
            start, end = match.start(), match.end()
            is_covered = any(start >= r_start and end <= r_end for r_start, r_end in covered_ranges)
            if is_covered:
                continue
            lemma = self._get_lemma_improved(word)
            if lemma in known_parts:
                is_not_capitalized = word[0].islower()
                is_not_start_of_text = match.start() > 0
                prev_char = text[match.start() - 1] if is_not_start_of_text else " "
                is_after_punctuation = prev_char in ".!?"

                if is_not_capitalized and is_not_start_of_text and not is_after_punctuation:
                    dbg(f"Skipping potential person part '{word}' due to lowercase.")
                    continue

                canonical_key = known_parts[lemma]
                entity_id = self.resolve_entity_id(canonical_key, "PER")

                new_entities.append(
                    {
                        "text": word,
                        "start": start,
                        "end": end,
                        "label": "PER",
                        "source": "person_parts_matcher",
                        "entity_id": entity_id,
                    }
                )
                dbg(f"Found known person part: '{word}' -> lemma '{lemma}'")
        return new_entities

    def get_fallback_persons(
        self, text: str, existing_orgs: list[dict[str, Any]]
    ) -> list[dict[str, Any]]:
        ents: list[dict[str, Any]] = []

        org_names = "|".join([re.escape(e["text"]) for e in existing_orgs])
        if org_names:
            pattern = re.compile(r"\b([А-ЯЁ][а-яё]{2,})\s+из\s+(?:" + org_names + r")\b")
            for m in pattern.finditer(text):
                ents.append(
                    {
                        "text": m.group(1),
                        "start": m.start(1),
                        "end": m.end(1),
                        "label": "PER",
                        "source": "fallback_per_context",
                    }
                )
                dbg(f"Fallback PER (context): '{m.group(1)}' at {m.start(1)}-{m.end(1)}")

        if FALLBACK_PER_NAMES:
            pattern_zovut = re.compile(
                r"(?i)(?:\bменя\b|\bего\b|\bеё\b|\bее\b|\bих\b)\s+зовут\s+([а-яё]{2,})\s+([а-яё]{2,})"
            )
            for m in pattern_zovut.finditer(text):
                ents.append(
                    {
                        "text": m.group(1) + " " + m.group(2),
                        "start": m.start(1),
                        "end": m.end(2),
                        "label": "PER",
                        "source": "fallback_per_zovut",
                    }
                )
        return ents

    def _normalize_label(self, label: str) -> str:
        label_mapping = {
            "PERSON": "PER",
            "GPE": "LOC",
            "PER": "PER",
            "ORG": "ORG",
            "LOC": "LOC",
        }
        return label_mapping.get(label, label)

    def merge_entities(self, *entity_lists) -> list[dict[str, Any]]:
        all_entities = []

        source_priority = {
            "regex_matcher": 7,
            "dictionary_compound": 6,
            "compound_matcher": 5,
            "dictionary": 4,
            "person_parts_matcher": 3.5,
            "natasha": 3,
            "fallback_per_context": 2.5,
            "fallback_per": 2,
            "spacy": 1,
        }

        for entities in entity_lists:
            all_entities.extend(entities)

        all_entities.sort(key=lambda x: x["start"])

        merged = []
        for entity in all_entities:
            overlaps = False
            for i, existing in enumerate(merged):
                if self._entities_overlap(entity, existing):
                    overlaps = True
                    if self._should_replace_entity(entity, existing, source_priority):
                        merged[i] = entity
                    break

            if not overlaps:
                merged.append(entity)

        return merged

    def _should_replace_entity(
        self, new_entity: dict, existing_entity: dict, source_priority: dict
    ) -> bool:
        new_priority = source_priority.get(new_entity["source"], 0)
        existing_priority = source_priority.get(existing_entity["source"], 0)

        if new_priority > existing_priority:
            return True
        elif new_priority < existing_priority:
            return False
        else:
            return len(new_entity["text"]) > len(existing_entity["text"])

    def _entities_overlap(self, ent1: dict, ent2: dict) -> bool:
        return not (ent1["end"] <= ent2["start"] or ent2["end"] <= ent1["start"])

    def filter_stopwords(self, entities: list[dict[str, Any]]) -> list[dict[str, Any]]:
        filtered = []

        for entity in entities:
            if not entity["text"].strip():
                dbg(f"Filtered empty entity: '{entity['text']}'")
                continue

            clean_text = re.sub(r"[^\w\s]", "", entity["text"]).strip()
            if not clean_text:
                dbg(f"Filtered punctuation-only entity: '{entity['text']}'")
                continue

            lemma = self._get_lemma_improved(entity["text"]).lower()

            if lemma not in self.stop_words:
                dbg(f"Keeping entity: '{entity['text']}' -> '{lemma}' ({entity['label']})")
                filtered.append(entity)
            else:
                dbg(f"Filtered stopword: '{entity['text']}' -> '{lemma}'")

        return filtered

    def get_canonical_key(self, entity_text: str, entity_label: str) -> str:
        text = entity_text.lower().strip()
        text = re.sub(r'["""«»\'\"]+', "", text)
        text = re.sub(r"\s+", " ", text).strip()

        if not text:
            return f"empty_{entity_label.lower()}"

        if text in self.alias_map:
            text = self.alias_map[text]

        lemmatized_text = self._get_lemma_improved(text)

        if not lemmatized_text.strip():
            return f"empty_{entity_label.lower()}_lemma"

        if entity_label == "PER":
            lemmas = lemmatized_text.split()
            if len(lemmas) > 1:
                lemmas.sort()
                lemmatized_text = " ".join(lemmas)

        return lemmatized_text

    def _get_lemma_improved(self, text: str) -> str:
        if not self.morph:
            return text.lower()

        words = text.split()
        lemmas = []

        for word in words:
            clean_word = re.sub(r"[^\w\s\-]", "", word)
            if clean_word:
                if "-" in clean_word:
                    parts = clean_word.split("-")
                    lemma_parts = []
                    for part in parts:
                        if part:
                            parsed = self.morph.parse(part)[0]
                            lemma_parts.append(parsed.normal_form)
                    if lemma_parts:
                        lemmas.append("-".join(lemma_parts))
                else:
                    parsed = self.morph.parse(clean_word)[0]
                    lemmas.append(parsed.normal_form)

        return " ".join(lemmas)

    def resolve_entity_id(self, canonical_key: str, entity_label: str) -> str:
        if canonical_key in self.mapping:
            return self.mapping[canonical_key]

        if entity_label == "PER":
            entity_id = self._resolve_person_coreference(canonical_key)
            if entity_id:
                return entity_id

        if entity_label in ["LOC", "ORG"]:
            entity_id = self._resolve_alias_coreference(canonical_key, entity_label)
            if entity_id:
                return entity_id

        return self._create_new_entity_id(canonical_key, entity_label)

    def _resolve_person_coreference(self, canonical_key: str) -> str:
        key_words = set(canonical_key.split())

        for existing_key, existing_id in list(self.mapping.items()):
            if existing_id.startswith("PERSON_"):
                existing_words = set(existing_key.split())

                if (
                    len(key_words) == 1
                    and len(existing_words) > 1
                    and key_words.issubset(existing_words)
                ):
                    return existing_id

                if (
                    len(existing_words) == 1
                    and len(key_words) > 1
                    and existing_words.issubset(key_words)
                ):
                    del self.mapping[existing_key]
                    self.mapping[canonical_key] = existing_id
                    return existing_id

        return None

    def _resolve_alias_coreference(self, canonical_key: str, entity_label: str) -> str:
        prefix = "LOCATION_" if entity_label == "LOC" else "ORG_"

        for existing_key, existing_id in self.mapping.items():
            if existing_id.startswith(prefix):
                if (
                    canonical_key in self.alias_map
                    and self.alias_map[canonical_key] == existing_key
                ):
                    return existing_id
                elif (
                    existing_key in self.alias_map and self.alias_map[existing_key] == canonical_key
                ):
                    return existing_id

        return None

    def _create_new_entity_id(self, canonical_key: str, entity_label: str) -> str:
        self.counters[entity_label] += 1

        label_prefixes = {"PER": "PERSON", "ORG": "ORG", "LOC": "LOCATION"}

        prefix = label_prefixes.get(entity_label, entity_label)
        entity_id = f"{prefix}_{self.counters[entity_label]}"

        self.mapping[canonical_key] = entity_id
        return entity_id

    def anonymize_text(self, text: str) -> tuple[str, dict[str, str]]:
        self.counters.clear()
        self.mapping.clear()

        dbg("Starting fixed ultimate anonymization pipeline")

        regex_entities = self.get_entities_from_regex(text)
        natasha_entities = self.get_entities_from_natasha(text)
        spacy_entities = self.get_entities_from_spacy(text) if HAS_SPACY else []
        dict_entities = self.get_entities_from_dictionary(text)
        compound_entities = self.find_compound_entities(text) if HAS_SPACY else []

        initial_sources = [
            regex_entities,
            natasha_entities,
            spacy_entities,
            dict_entities,
            compound_entities,
        ]

        first_pass_entities = self.merge_entities(*initial_sources)

        org_entities = [e for e in first_pass_entities if e["label"] == "ORG"]
        fallback_pers = self.get_fallback_persons(text, org_entities)

        all_found_entities = self.merge_entities(first_pass_entities, fallback_pers)

        known_parts_entities = self.find_known_person_parts(text, all_found_entities)

        final_merged_entities = self.merge_entities(all_found_entities, known_parts_entities)

        final_entities = self.filter_stopwords(final_merged_entities)

        dbg(f"Final entities after filtering: {len(final_entities)}")

        replacements: list[dict[str, Any]] = []
        for entity in final_entities:
            canonical_key = self.get_canonical_key(entity["text"], entity["label"])
            entity_id = self.resolve_entity_id(canonical_key, entity["label"])
            replacements.append(
                {
                    "start": entity["start"],
                    "end": entity["end"],
                    "original": entity["text"],
                    "replacement": f"[{entity['label']}: {entity['text']}]",
                    "entity_id": entity_id,
                    "label": entity["label"],
                }
            )

        replacements.sort(key=lambda x: x["start"], reverse=True)
        anonymized_text = text
        for replacement in replacements:
            start, end = replacement["start"], replacement["end"]
            anonymized_text = (
                anonymized_text[:start] + replacement["replacement"] + anonymized_text[end:]
            )

        final_mapping = {v: k for k, v in self.mapping.items()}
        return anonymized_text, final_mapping


def main():
    try:
        dbg(f"py_version={sys.version.splitlines()[0]}")
        dbg(f"cwd={os.getcwd()}")
        dbg(f"env_VERBOSE={VERBOSE} env_FALLBACK_PER_NAMES={FALLBACK_PER_NAMES}")
        dbg(f"HAS_PYMORPHY={HAS_PYMORPHY} HAS_SPACY={HAS_SPACY}")
    except Exception:
        pass

    t0 = time.time()

    if len(sys.argv) > 1:
        input_filepath = sys.argv[1]
        dbg(f"Reading input from file: {input_filepath}")
        try:
            with open(input_filepath, encoding="utf-8") as f:
                data = f.read()
        except Exception as e:
            dbg(f"Failed to read input file: {e}")
            return 1
    else:
        dbg("Reading input from stdin")
        data = sys.stdin.read()

    if data and data[0] == "\ufeff":
        data = data.lstrip("\ufeff")
    if not data:
        return 0

    try:
        _prev_in = data[:160].replace("\n", " ").replace("\r", " ")
        dbg(f'input_len={len(data)} preview="{_prev_in}"')
    except Exception:
        pass

    try:
        anonymizer = FixedUltimateAnonymizer()
        anonymized_text, mapping = anonymizer.anonymize_text(data)

        try:
            _prev_out = anonymized_text[:160].replace("\n", " ").replace("\r", " ")
            dbg(f'output_len={len(anonymized_text)} preview="{_prev_out}"')
            dbg(f"total_ms={int((time.time() - t0) * 1000)}")
            dbg(f"entities_mapped={len(mapping)}")
        except Exception:
            pass

        sys.stdout.write(anonymized_text)
        return 0

    except Exception as e:
        dbg(f"fixed_anonymizer_error={e}")
        sys.stdout.write(data)
        return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:
        try:
            sys.stderr.write(f"natasha_redact error: {e}\n")
        except Exception:
            pass
        sys.exit(1)
