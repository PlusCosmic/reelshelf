import { type KeyboardEvent, useEffect, useRef, useState } from "react";
import { getGoogleSuggestions } from "../services/googleSearch";
import { Autocomplete } from "@mantine/core";
import "./GoogleSearchBar.module.scss";
import { GoogleIcon } from "./GoogleIcon";
import { IconSearch } from "@tabler/icons-react";
import { useFocusTrap } from "@mantine/hooks";

export default function GoogleSearchBar() {
  const [query, setQuery] = useState("");
  const [suggestions, setSuggestions] = useState<string[]>([]);
  const inputRef = useRef<HTMLInputElement>(null);
  const searchIcon = <IconSearch size={16} />;
  const focusTrapRef = useFocusTrap();
  // Function to fetch suggestions from Google's API
  useEffect(() => {
    const fetchSuggestions = async () => {
      if (query.trim() === "") {
        setSuggestions([]);
        return;
      }

      try {
        // Using Google's suggestion API with JSONP to avoid CORS issues
        const suggestions = await getGoogleSuggestions(query);
        setSuggestions(suggestions);
      } catch (error) {
        console.error("Error fetching suggestions:", error);
        setSuggestions([]);
      }
    };

    // Debounce the API call
    const timeoutId = setTimeout(() => {
      fetchSuggestions();
    }, 300);

    return () => clearTimeout(timeoutId);
  }, [query]);

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
