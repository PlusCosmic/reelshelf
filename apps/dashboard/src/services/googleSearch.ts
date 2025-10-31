/**
 * Service for Google search suggestions using JSONP to avoid CORS issues
 */

// Define the type for Google's suggestion response
interface GoogleSuggestionResponse {
  [0]: string; // The search query
  [1]: string[]; // The suggestions
}

// Extend the Window interface to allow our dynamic callback property
declare global {
  interface Window {
    [key: string]: unknown;
  }
}

/**
 * Fetches search suggestions from Google using JSONP
 * @param query The search query
 * @returns A promise that resolves to an array of suggestion strings
 */
export async function getGoogleSuggestions(query: string): Promise<string[]> {
  if (!query.trim()) return [];

  return new Promise((resolve, reject) => {
    // Create a unique callback name
    const callbackName = `googleSuggestCallback_${Date.now()}`;

    // Create script element
    const script = document.createElement("script");
    const googleUrl = `https://suggestqueries.google.com/complete/search?client=firefox&q=${encodeURIComponent(query)}&callback=${callbackName}`;

    // Set timeout to clean up if the request takes too long
    const timeoutId = setTimeout(() => {
      cleanup();
      reject(new Error("Request timed out"));
    }, 5000);

    // Define the callback function
    window[callbackName] = (data: GoogleSuggestionResponse) => {
      cleanup();
      resolve(data[1].slice(0, 5)); // Take only first 5 suggestions
    };

    // Function to clean up resources
    function cleanup() {
      // Remove the script element
      if (script.parentNode) {
        script.parentNode.removeChild(script);
      }

      // Remove the callback function
      delete window[callbackName];

      // Clear the timeout
      clearTimeout(timeoutId);
    }

    // Set up error handling
    script.onerror = () => {
      cleanup();
      reject(new Error("Failed to load suggestions"));
    };

    // Add the script to the document to start the request
    script.src = googleUrl;
    document.body.appendChild(script);
  });
}
