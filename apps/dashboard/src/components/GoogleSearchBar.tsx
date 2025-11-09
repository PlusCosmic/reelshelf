import { type KeyboardEvent, useEffect, useRef, useState } from "react";
import { Autocomplete } from "@mantine/core";
import "./GoogleSearchBar.module.scss";
import { GoogleIcon } from "./GoogleIcon";
import { IconSearch } from "@tabler/icons-react";
import { useFocusTrap } from "@mantine/hooks";
import { useDebouncedValue } from "@repo/shared/hooks/useDebouncedValue";
import { useGoogleSuggestions } from "../hooks/queries";

export default function GoogleSearchBar() {
  const [query, setQuery] = useState("");
  const [debouncedQuery] = useDebouncedValue(query, 300);
  const { data: suggestions = [] } = useGoogleSuggestions(debouncedQuery);
  const inputRef = useRef<HTMLInputElement>(null);
  const searchIcon = <IconSearch size={16} />;
  const focusTrapRef = useFocusTrap();

  // Focus the input field when component mounts
  useEffect(() => {
    if (inputRef.current) {
      inputRef.current.focus();
    }
  }, []);

  const handleSubmit = () => {
    if (query.trim()) {
      window.open(
        `https://www.google.com/search?q=${encodeURIComponent(query)}`,
        "_self",
      );
    }
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      handleSubmit();
    }
  };

  return (
    <Autocomplete
      ref={focusTrapRef}
      size="lg"
      radius="lg"
      data={suggestions}
      leftSectionPointerEvents="none"
      leftSection={<GoogleIcon />}
      rightSection={searchIcon}
      rightSectionPointerEvents="none"
      value={query}
      onChange={setQuery}
      onKeyDown={handleKeyDown}
      onOptionSubmit={handleSubmit}
      onClear={() => setQuery("")}
      placeholder="Search Google..."
    />
  );
}
