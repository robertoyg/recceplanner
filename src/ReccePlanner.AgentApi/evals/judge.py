"""
LLM-as-judge infrastructure for ReccePlanner agent evals.

The judge uses a cheap, fast Claude model to evaluate whether an agent
response meets a set of plain-English criteria. Each criterion is scored
independently so partial passes are visible.

Usage:
    result = await judge(
        scenario="User uploads rally data with no travel times",
        conversation=[
            {"role": "user", "content": "Here is my data..."},
            {"role": "assistant", "content": "I can see 7 stages..."},
        ],
        criteria=[
            "Agent explicitly states that travel times are missing",
            "Agent asks the user to provide travel times",
            "Agent does NOT attempt to run the optimizer",
        ]
    )
    assert result.passed
    print(result.summary())
"""

import json
import os
from dataclasses import dataclass, field

import anthropic

# Use the cheapest model for judging — speed and cost matter more than raw quality here
JUDGE_MODEL = "claude-haiku-4-5-20251001"

JUDGE_SYSTEM = """You are an expert evaluator for a rally recce planning AI assistant.
You will be shown a conversation and a list of criteria. For each criterion, decide
whether the assistant's response satisfies it. Be strict but fair.

Respond with valid JSON only — no prose outside the JSON block."""

JUDGE_PROMPT_TEMPLATE = """## Scenario
{scenario}

## Conversation
{conversation}

## Criteria to evaluate
{criteria_list}

Respond with this exact JSON structure:
{{
  "criteria_results": [
    {{"criterion": "<criterion text>", "met": true|false, "note": "<brief reason>"}}
  ],
  "overall_verdict": "pass"|"partial"|"fail",
  "overall_note": "<one sentence summary>"
}}

Rules:
- "pass"    = ALL criteria met
- "partial" = some criteria met, some not
- "fail"    = most or all criteria not met
"""


@dataclass
class CriterionResult:
    criterion: str
    met: bool
    note: str


@dataclass
class EvalResult:
    scenario: str
    criteria_results: list[CriterionResult]
    overall_verdict: str   # "pass" | "partial" | "fail"
    overall_note: str
    raw_judge_response: str = field(repr=False, default="")

    @property
    def passed(self) -> bool:
        return self.overall_verdict == "pass"

    @property
    def score(self) -> float:
        if not self.criteria_results:
            return 0.0
        return sum(1 for c in self.criteria_results if c.met) / len(self.criteria_results)

    def summary(self) -> str:
        lines = [
            f"Scenario : {self.scenario}",
            f"Verdict  : {self.overall_verdict.upper()}  ({self.score:.0%} criteria met)",
            f"Note     : {self.overall_note}",
            "",
        ]
        for cr in self.criteria_results:
            mark = "[PASS]" if cr.met else "[FAIL]"
            lines.append(f"  {mark} {cr.criterion}")
            if cr.note:
                lines.append(f"         -> {cr.note}")
        return "\n".join(lines)


async def judge(
    scenario: str,
    conversation: list[dict],
    criteria: list[str],
    client: anthropic.AsyncAnthropic | None = None,
) -> EvalResult:
    """
    Ask the LLM judge to evaluate the conversation against the criteria.

    `conversation` is a list of {"role": "user"|"assistant", "content": "..."} dicts.
    `criteria` is a list of plain-English pass/fail statements.
    """
    _client = client or anthropic.AsyncAnthropic(api_key=os.environ["ANTHROPIC_API_KEY"])
    _owned = client is None  # close if we created it

    conv_text = "\n".join(
        f"**{m['role'].title()}:** {m['content']}" for m in conversation
    )
    criteria_list = "\n".join(f"{i+1}. {c}" for i, c in enumerate(criteria))

    prompt = JUDGE_PROMPT_TEMPLATE.format(
        scenario=scenario,
        conversation=conv_text,
        criteria_list=criteria_list,
    )

    try:
        response = await _client.messages.create(
            model=JUDGE_MODEL,
            max_tokens=1024,
            system=JUDGE_SYSTEM,
            messages=[{"role": "user", "content": prompt}],
        )
        raw = response.content[0].text.strip()

        # Strip markdown code fences if present
        if raw.startswith("```"):
            raw = raw.split("```")[1]
            if raw.startswith("json"):
                raw = raw[4:]
            raw = raw.strip()

        parsed = json.loads(raw)

        criteria_results = [
            CriterionResult(
                criterion=cr["criterion"],
                met=bool(cr["met"]),
                note=cr.get("note", ""),
            )
            for cr in parsed["criteria_results"]
        ]

        return EvalResult(
            scenario=scenario,
            criteria_results=criteria_results,
            overall_verdict=parsed["overall_verdict"],
            overall_note=parsed["overall_note"],
            raw_judge_response=raw,
        )

    finally:
        if _owned:
            await _client.aclose()
