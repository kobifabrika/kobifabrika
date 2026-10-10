import {
  Car,
  Coffee,
  Dumbbell,
  LayoutGrid,
  Scissors,
  Sparkles,
  Stethoscope,
  Store,
  type LucideIcon,
} from "lucide-react";

import type { Sector } from "@/lib/api/types";

/** Sihirbazdaki sektör kartları. Ad ve şablon backend SectorCatalog ile aynı (GET /api/sectors). */
export const sectors: { value: Sector; label: string; icon: LucideIcon; template: "Hizmet" | "Perakende" | "Genel" }[] = [
  { value: "HairBarber", label: "Kuaför/Berber", icon: Scissors, template: "Hizmet" },
  { value: "BeautyCare", label: "Güzellik ve Bakım", icon: Sparkles, template: "Hizmet" },
  { value: "ClinicHealth", label: "Klinik/Sağlık", icon: Stethoscope, template: "Hizmet" },
  { value: "SportsFitness", label: "Spor ve Fitness", icon: Dumbbell, template: "Hizmet" },
  { value: "AutoService", label: "Oto Servis", icon: Car, template: "Hizmet" },
  { value: "RetailStore", label: "Perakende/Mağaza", icon: Store, template: "Perakende" },
  { value: "CafeRestaurant", label: "Kafe/Restoran", icon: Coffee, template: "Perakende" },
  { value: "Other", label: "Diğer", icon: LayoutGrid, template: "Genel" },
];

export const templateNames = { Service: "Hizmet", Retail: "Perakende", General: "Genel" } as const;
