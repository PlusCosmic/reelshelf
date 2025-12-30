import { type KeyboardEvent, useEffect, useRef, useState } from "react";
import {
  Autocomplete,
  Box,
  CloseButton,
  Loader,
  Text,
  Transition,
  type AutocompleteProps,
} from "@mantine/core";
import { GoogleIcon } from "./GoogleIcon";
import { IconSearch, IconWorld, IconArrowUpRight } from "@tabler/icons-react";
import { useFocusTrap } from "@mantine/hooks";
import { useDebouncedValue } from "@repo/shared/hooks/useDebouncedValue";
import { useGoogleSuggestions } from "../hooks/queries";

// URL detection regex - matches common URL patterns
const URL_REGEX = /^(https?:\/\/)?([a-zA-Z0-9-]+\.)+[a-zA-Z]{2,}(\/.*)?$/;
const PROTOCOL_REGEX = /^https?:\/\//;

function isValidUrl(input: string): boolean {
  const trimmed = input.trim();
  return URL_REGEX.test(trimmed);
}

function formatUrl(input: string): string {
  const trimmed = input.trim();
  if (PROTOCOL_REGEX.test(trimmed)) {
    return trimmed;
  }
  return `https://${trimmed}`;
}

interface SuggestionItemProps {
  value: string;
  isUrl?: boolean;
}

function renderAutocompleteOption({
  option,
}: {
  option: SuggestionItemProps;
}): React.ReactNode {
  return (
    <Box
      style={{
        display: "flex",
        alignItems: "center",
        gap: "12px",
        width: "100%",
        padding: "4px 0",
      }}
    >
      {option.isUrl ? (
        <IconWorld
          size={16}
          style={{ opacity: 0.6, flexShrink: 0 }}
          stroke={1.5}
        />
      ) : (
        <IconSearch
          size={16}
          style={{ opacity: 0.6, flexShrink: 0 }}
          stroke={1.5}
        />
      )}
      <Text
        size="sm"
        style={{
          flex: 1,
          overflow: "hidden",
          textOverflow: "ellipsis",
          whiteSpace: "nowrap",
        }}
      >
        {option.value}
      </Text>
      {option.isUrl && (
        <IconArrowUpRight
          size={14}
          style={{ opacity: 0.4, flexShrink: 0 }}
          stroke={1.5}
        />
      )}
    </Box>
  );
}

export default function GoogleSearchBar() {
  const [query, setQuery] = useState("");
  const [isFocused, setIsFocused] = useState(false);
  const [debouncedQuery] = useDebouncedValue(query, 300);
  const {
    data: suggestions = [],
    isFetching,
    isLoading,
  } = useGoogleSuggestions(debouncedQuery);
  const inputRef = useRef<HTMLInputElement>(null);
  const focusTrapRef = useFocusTrap();

  const isUrl = isValidUrl(query);
  const showLoading = (isFetching || isLoading) && query.trim().length > 0;

  // Build autocomplete options with proper typing
  const autocompleteOptions: SuggestionItemProps[] = [];

  // If it looks like a URL, show it as the first option
  if (isUrl && query.trim()) {
    autocompleteOptions.push({
      value: query.trim(),
      isUrl: true,
    });
  }

  // Add Google suggestions
  suggestions.forEach((suggestion) => {
    autocompleteOptions.push({
      value: suggestion,
      isUrl: false,
    });
  });

  // Focus the input field when component mounts
  useEffect(() => {
    if (inputRef.current) {
      inputRef.current.focus();
    }
  }, []);

  const handleSubmit = (value?: string) => {
    const searchQuery = value ?? query;
    if (searchQuery.trim()) {
      if (isValidUrl(searchQuery)) {
        // Navigate directly to URL
        window.open(formatUrl(searchQuery), "_self");
      } else {
        // Search Google
        window.open(
          `https://www.google.com/search?q=${encodeURIComponent(searchQuery)}`,
          "_self"
        );
      }
    }
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      handleSubmit();
    } else if (e.key === "Escape") {
      if (query) {
        setQuery("");
        e.preventDefault();
      } else {
        inputRef.current?.blur();
      }
    }
  };

  const handleClear = () => {
    setQuery("");
    inputRef.current?.focus();
  };

  const handleSearchClick = () => {
    handleSubmit();
  };

  // Right section with loading indicator, clear button, or search icon
  const rightSection = (
    <Box
      style={{
        display: "flex",
        alignItems: "center",
        gap: "4px",
        paddingRight: "4px",
      }}
    >
      <Transition mounted={showLoading} transition="fade" duration={150}>
        {(styles) => (
          <Box style={{ ...styles, display: "flex", alignItems: "center" }}>
            <Loader size={16} color="gray" />
          </Box>
        )}
      </Transition>
      <Transition
        mounted={query.length > 0 && !showLoading}
        transition="fade"
        duration={150}
      >
        {(styles) => (
          <CloseButton
            size="sm"
            radius="xl"
            onClick={handleClear}
            style={{
              ...styles,
              color: "rgba(255, 255, 255, 0.5)",
              "&:hover": {
                backgroundColor: "rgba(255, 255, 255, 0.1)",
              },
            }}
            aria-label="Clear search"
          />
        )}
      </Transition>
      <Box
        component="button"
        onClick={handleSearchClick}
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          padding: "6px",
          borderRadius: "50%",
          border: "none",
          background: isFocused
            ? "rgba(255, 255, 255, 0.1)"
            : "rgba(255, 255, 255, 0.05)",
          cursor: "pointer",
          transition: "all 0.2s ease",
          color: "rgba(255, 255, 255, 0.7)",
        }}
        aria-label="Search"
      >
        <IconSearch size={18} stroke={2} />
      </Box>
    </Box>
  );

  return (
    <Autocomplete
      ref={focusTrapRef}
      size="xl"
      radius="xl"
      data={autocompleteOptions as AutocompleteProps["data"]}
      renderOption={renderAutocompleteOption}
      leftSectionPointerEvents="none"
      leftSection={<GoogleIcon />}
      rightSection={rightSection}
      rightSectionWidth={query.length > 0 ? 90 : 50}
      rightSectionPointerEvents="all"
      value={query}
      onChange={setQuery}
      onKeyDown={handleKeyDown}
      onOptionSubmit={handleSubmit}
      onFocus={() => setIsFocused(true)}
      onBlur={() => setIsFocused(false)}
      placeholder="Search Google or type a URL..."
      maxDropdownHeight={300}
      filter={({ options }) => options}
      styles={{
        root: {
          transition: "all 0.3s cubic-bezier(0.4, 0, 0.2, 1)",
        },
        wrapper: {
          transition: "all 0.3s cubic-bezier(0.4, 0, 0.2, 1)",
        },
        input: {
          paddingLeft: "3rem",
          paddingRight: "5rem",
          fontSize: "1rem",
          fontWeight: 500,
          backgroundColor: isFocused
            ? "rgba(255, 255, 255, 0.08)"
            : "rgba(255, 255, 255, 0.05)",
          border: isFocused
            ? "1px solid rgba(255, 255, 255, 0.25)"
            : "1px solid rgba(255, 255, 255, 0.1)",
          backdropFilter: "blur(12px)",
          transition: "all 0.3s cubic-bezier(0.4, 0, 0.2, 1)",
          transform: isFocused ? "translateY(-2px)" : "translateY(0)",
          boxShadow: isFocused
            ? "0 8px 32px rgba(0, 0, 0, 0.2), 0 0 0 1px rgba(255, 255, 255, 0.05)"
            : "0 2px 8px rgba(0, 0, 0, 0.1)",
          "&::placeholder": {
            color: "rgba(255, 255, 255, 0.4)",
            transition: "color 0.2s ease",
          },
          "&:focus::placeholder": {
            color: "rgba(255, 255, 255, 0.3)",
          },
        },
        dropdown: {
          backgroundColor: "rgba(25, 25, 25, 0.98)",
          backdropFilter: "blur(16px)",
          border: "1px solid rgba(255, 255, 255, 0.12)",
          borderRadius: "16px",
          boxShadow:
            "0 16px 48px rgba(0, 0, 0, 0.3), 0 0 0 1px rgba(255, 255, 255, 0.05)",
          padding: "8px",
          marginTop: "8px",
        },
        option: {
          borderRadius: "10px",
          padding: "10px 14px",
          transition: "all 0.15s ease",
          "&[data-combobox-selected]": {
            backgroundColor: "rgba(255, 255, 255, 0.12)",
          },
          "&:hover": {
            backgroundColor: "rgba(255, 255, 255, 0.08)",
          },
        },
      }}
    />
  );
}
