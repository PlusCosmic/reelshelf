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
      size="xl"
      radius="xl"
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
      placeholder="Search Google or type a URL..."
      styles={{
        input: {
          paddingLeft: '3rem',
          fontSize: '1rem',
          fontWeight: 500,
          backgroundColor: 'rgba(255, 255, 255, 0.05)',
          border: '1px solid rgba(255, 255, 255, 0.1)',
          backdropFilter: 'blur(10px)',
          transition: 'all 0.2s ease',
          '&:focus': {
            backgroundColor: 'rgba(255, 255, 255, 0.08)',
            border: '1px solid rgba(255, 255, 255, 0.2)',
            transform: 'translateY(-2px)',
            boxShadow: '0 4px 12px rgba(0, 0, 0, 0.1)',
          },
          '&::placeholder': {
            color: 'rgba(255, 255, 255, 0.5)',
          }
        },
        dropdown: {
          backgroundColor: 'rgba(30, 30, 30, 0.95)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(255, 255, 255, 0.1)',
        },
        option: {
          '&[data-combobox-selected]': {
            backgroundColor: 'rgba(255, 255, 255, 0.1)',
          },
          '&:hover': {
            backgroundColor: 'rgba(255, 255, 255, 0.05)',
          }
        }
      }}
    />
  );
}
