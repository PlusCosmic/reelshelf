/**
 * Keeps user-facing copy in `src/copy/` (see `src/copy/README.md`): reports words written straight into JSX,
 * whether as text, a string or template in `{...}`, or a string given to an attribute people read
 * (`aria-label`, `title`, `placeholder` and the like). Text without letters (punctuation, numbers, `·`) is
 * allowed, and so are attributes that aren't read, such as `className` or `type`.
 */
const hasLetter = (text) => /\p{L}/u.test(text);

const defaultAttributes = [
  "alt",
  "aria-description",
  "aria-label",
  "aria-placeholder",
  "aria-roledescription",
  "aria-valuetext",
  "description",
  "label",
  "placeholder",
  "title",
];

/** The string literals and templates an expression can evaluate to directly. */
function* literalsIn(node) {
  if (!node) return;
  switch (node.type) {
    case "Literal":
      if (typeof node.value === "string" && hasLetter(node.value)) yield node;
      break;
    case "TemplateLiteral":
      if (node.quasis.some((quasi) => hasLetter(quasi.value.cooked ?? "")))
        yield node;
      break;
    case "ConditionalExpression":
      yield* literalsIn(node.consequent);
      yield* literalsIn(node.alternate);
      break;
    case "LogicalExpression":
      yield* literalsIn(node.left);
      yield* literalsIn(node.right);
      break;
    case "TSAsExpression":
    case "TSSatisfiesExpression":
      yield* literalsIn(node.expression);
      break;
  }
}

export default {
  meta: {
    type: "suggestion",
    docs: {
      description: "Disallow user-facing copy written directly in JSX",
    },
    schema: [
      {
        type: "object",
        properties: {
          attributes: { type: "array", items: { type: "string" } },
        },
        additionalProperties: false,
      },
    ],
    messages: {
      text: "Move this copy into src/copy/ and import it (see src/copy/README.md).",
      attribute:
        "Move the `{{name}}` copy into src/copy/ and import it (see src/copy/README.md).",
    },
  },
  create(context) {
    const attributes = new Set(
      context.options[0]?.attributes ?? defaultAttributes,
    );
    const isChild = (node) =>
      node.parent?.type === "JSXElement" || node.parent?.type === "JSXFragment";

    return {
      JSXText(node) {
        if (hasLetter(node.value))
          context.report({ node, messageId: "text" });
      },
      JSXExpressionContainer(node) {
        if (!isChild(node)) return;
        for (const literal of literalsIn(node.expression))
          context.report({ node: literal, messageId: "text" });
      },
      JSXAttribute(node) {
        if (node.name.type !== "JSXIdentifier") return;
        const name = node.name.name;
        if (!attributes.has(name) || !node.value) return;
        const value =
          node.value.type === "JSXExpressionContainer"
            ? node.value.expression
            : node.value;
        for (const literal of literalsIn(value))
          context.report({
            node: literal,
            messageId: "attribute",
            data: { name },
          });
      },
    };
  },
};
