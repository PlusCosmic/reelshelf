import { RuleTester } from "eslint";
import tseslint from "typescript-eslint";
import { afterAll, describe, it } from "vitest";
import rule from "./no-jsx-copy.js";

RuleTester.afterAll = afterAll;
RuleTester.describe = describe;
RuleTester.it = it;

const tester = new RuleTester({
  languageOptions: {
    parser: tseslint.parser,
    parserOptions: { ecmaFeatures: { jsx: true } },
  },
});

tester.run("no-jsx-copy", rule, {
  valid: [
    "<p>{copy.title}</p>",
    "<p>{copy.count(3)}</p>",
    '<span>{" "}·{" "}</span>',
    "<span>42 · 3</span>",
    '<button type="button" className="rs-button" />',
    "<input aria-label={copy.search} />",
    "<p>{`${count}`}</p>",
    'const label = "Not in JSX";',
  ],
  invalid: [
    { code: "<p>Hello</p>", errors: [{ messageId: "text" }] },
    { code: '<p>{"Hello"}</p>', errors: [{ messageId: "text" }] },
    { code: "<p>{`${n} clips`}</p>", errors: [{ messageId: "text" }] },
    {
      code: '<p>{busy ? "Saving…" : copy.save}</p>',
      errors: [{ messageId: "text" }],
    },
    {
      code: '<p>{error && "It failed"}</p>',
      errors: [{ messageId: "text" }],
    },
    {
      code: '<input placeholder="Search" />',
      errors: [{ messageId: "attribute", data: { name: "placeholder" } }],
    },
    {
      code: "<button aria-label={`Switch to ${next} mode`} />",
      errors: [{ messageId: "attribute", data: { name: "aria-label" } }],
    },
    {
      code: '<Dialog title={open ? "Rename" : "Create"} />',
      errors: [{ messageId: "attribute" }, { messageId: "attribute" }],
    },
  ],
});
