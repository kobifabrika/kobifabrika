"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { QueryError } from "@/components/feedback/query-error";
import { useSession } from "@/components/providers/session-provider";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Switch } from "@/components/ui/switch";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { can } from "@/lib/permissions";

import { calendarKey } from "./appointment-bits";

const settingsKey = ["business", "appointment-settings"] as const;

/** Ayarlar → Randevu → Takvim: personelin takvim görünürlüğü (online randevu ayarları BookingSettingsCard'da). */
export function AppointmentSettings() {
  const { role } = useSession();
  const readOnly = !can(role, "manageBusiness");
  const queryClient = useQueryClient();
  const settings = useQuery({ queryKey: settingsKey, queryFn: () => unwrap(api.GET("/api/business/appointment-settings")) });
  const save = useMutation({
    mutationFn: (staffSeesOnlyOwn: boolean) => unwrap(api.PUT("/api/business/appointment-settings", { body: { staffSeesOnlyOwn } })),
    onSuccess: async (next) => {
      queryClient.setQueryData(settingsKey, next);
      await queryClient.invalidateQueries({ queryKey: calendarKey });
      toast.success("Randevu ayarları kaydedildi.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">Takvim</CardTitle>
        <CardDescription>Personel rolündeki kullanıcıların takvimde neyi göreceği.</CardDescription>
      </CardHeader>
      <CardContent>
        {settings.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : settings.isError ? (
          <QueryError error={settings.error} onRetry={settings.refetch} />
        ) : (
          <label className="flex min-h-14 items-center justify-between gap-4">
            <span className="flex flex-col">
              <span className="text-sm font-semibold text-black">Personel sadece kendi randevularını görsün</span>
              <span className="text-sm text-neutral-600">
                Kapalıyken personel bütün takvimi görür, herkes için randevu açar ve taşır (resepsiyonu personel yapıyorsa). Açıkken sadece hesabının
                bağlı olduğu personelin randevularını görür ve yazar.
              </span>
            </span>
            <Switch checked={settings.data.staffSeesOnlyOwn} disabled={readOnly || save.isPending} onCheckedChange={(v) => save.mutate(v)} />
          </label>
        )}
      </CardContent>
    </Card>
  );
}
