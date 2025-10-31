import { apiConfig } from "../config/apiConfig.ts";
import {
  Configuration,
  type MapInfo,
  NucleusApi,
} from "@pluscosmic/nucleus-api-client";

// Define the types for our application's use of the API data
export interface ApexMapInfo {
  map: string;
  start: string; // ISO date string
  end: string; // ISO date string
  remainingMins: number;
  imageUrl: string;
}

export interface ApexMapRotation {
  standard: {
    current: ApexMapInfo;
    next: ApexMapInfo;
  };
  ranked: {
    current: ApexMapInfo;
    next: ApexMapInfo;
  };
  lastUpdated: string; // ISO date string
}

// Helper function to calculate remaining minutes
function calculateRemainingMinutes(endTime: Date | string): number {
  const end = new Date(endTime).getTime();
  const now = new Date().getTime();
  const diffMs = end - now;
  return Math.max(0, Math.floor(diffMs / (1000 * 60)));
}

function convertRawMapInfo(raw: MapInfo): ApexMapInfo {
  return {
    map: raw.name,
    start: raw.mapStart.toString(),
    end: raw.mapEnd.toString(),
    remainingMins: calculateRemainingMinutes(raw.mapEnd),
    imageUrl: raw.assetUri,
  };
}

// Fetch map rotation data from the API
export async function fetchMapRotation(): Promise<ApexMapRotation> {
  const api = new NucleusApi(
    new Configuration({ basePath: apiConfig.baseUrl }),
  );
  try {
    const res = await api.getApexMapRotation();
    return {
      standard: {
        current: convertRawMapInfo(res.standardMap),
        next: convertRawMapInfo(res.standardMapNext),
      },
      ranked: {
        current: convertRawMapInfo(res.rankedMap),
        next: convertRawMapInfo(res.rankedMapNext),
      },
      lastUpdated: res.correctAsOf.toString(),
    };
  } catch (err) {
    console.error("Error fetching Apex Legends map rotation", err);
    throw new Error("Failed to fetch Apex Legends map rotation data");
  }
}
