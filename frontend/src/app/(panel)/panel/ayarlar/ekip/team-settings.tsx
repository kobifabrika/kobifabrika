"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { MailPlus, RotateCw, Trash2, UserMinus } from "lucide-react";
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";

import { Field, FormMessage } from "@/components/forms/field";
import { RequirePermission } from "@/components/panel/settings-nav";
import { useSession } from "@/components/providers/session-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { roleDescriptions, roleNames, type Invitation, type Member, type Role } from "@/lib/api/types";
import { formatDate } from "@/lib/format";
import { emailSchema } from "@/lib/validation/rules";

const membersKey = ["team", "members"] as const;
const invitationsKey = ["team", "invitations"] as const;
const allRoles: Role[] = ["Owner", "Manager", "Staff", "Accountant"];

const selectClass =
  "h-11 w-full rounded-lg border border-input bg-background px-3 text-base text-black outline-none focus-visible:border-foreground focus-visible:ring-3 focus-visible:ring-ring";

/** Owner rolünü sadece Owner verir/alır (backend TeamEndpoints ile aynı kural). */
function assignableRoles(myRole: Role): Role[] {
  return myRole === "Owner" ? allRoles : allRoles.filter((r) => r !== "Owner");
}

export function TeamSettings() {
  return (
    <RequirePermission permission="manageTeam">
      <div className="flex flex-col gap-6">
        <InviteCard />
        <InvitationsCard />
        <MembersCard />
      </div>
    </RequirePermission>
  );
}

function InviteCard() {
  const { me, role } = useSession();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const schema = z.object({ email: emailSchema, role: z.enum(["Owner", "Manager", "Staff", "Accountant"]) });
  const form = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema), defaultValues: { email: "", role: "Staff" } });
  const selectedRole = useWatch({ control: form.control, name: "role" });
  const verified = me.user.emailConfirmed;

  const onSubmit = form.handleSubmit(async (values) => {
    setError(null);
    try {
      await unwrap(api.POST("/api/team/invitations", { body: values }));
      toast.success(`${values.email} adresine davet gönderildi.`);
      form.reset({ email: "", role: values.role });
      await queryClient.invalidateQueries({ queryKey: invitationsKey });
    } catch (e) {
      const fields = fieldErrors(e);
      if (fields.email) form.setError("email", { message: fields.email });
      else setError(errorMessage(e));
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">Ekibe davet et</CardTitle>
        <CardDescription>Davet bağlantısı 7 gün geçerlidir ve tek kullanımlıktır. Mali müşavir kullanıcı sınırına sayılmaz.</CardDescription>
      </CardHeader>
      <CardContent>
        {!verified && (
          <div className="mb-4">
            <FormMessage tone="info">Davet göndermek için önce e-posta adresini doğrula (sayfanın üstündeki uyarı).</FormMessage>
          </div>
        )}
        <form className="flex flex-col gap-4" onSubmit={onSubmit} noValidate>
          <div className="grid gap-4 sm:grid-cols-[1fr_12rem]">
            <Field label="E-posta" error={form.formState.errors.email?.message}>
              <Input type="email" inputMode="email" placeholder="ornek@isletme.com" disabled={!verified} {...form.register("email")} />
            </Field>
            <Field label="Rol">
              <select className={selectClass} disabled={!verified} {...form.register("role")}>
                {assignableRoles(role).map((r) => (
                  <option key={r} value={r}>
                    {roleNames[r]}
                  </option>
                ))}
              </select>
            </Field>
          </div>
          <p className="text-sm text-neutral-600">
            <span className="font-semibold text-black">{roleNames[selectedRole]}:</span> {roleDescriptions[selectedRole]}
          </p>
          {error && <FormMessage>{error}</FormMessage>}
          <Button type="submit" size="lg" className="self-start" disabled={!verified || form.formState.isSubmitting}>
            <MailPlus aria-hidden className="size-4" />
            {form.formState.isSubmitting ? "Gönderiliyor…" : "Davet gönder"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

function InvitationsCard() {
  const { role } = useSession();
  const queryClient = useQueryClient();
  const invitations = useQuery({ queryKey: invitationsKey, queryFn: () => unwrap(api.GET("/api/team/invitations")) });
  const refresh = () => queryClient.invalidateQueries({ queryKey: invitationsKey });

  const resend = useMutation({
    mutationFn: (id: string) => unwrap(api.POST("/api/team/invitations/{id}/resend", { params: { path: { id } } })),
    onSuccess: () => toast.success("Yeni davet bağlantısı gönderildi."),
    onError: (e) => toast.error(errorMessage(e)),
    onSettled: refresh,
  });
  const revoke = useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/team/invitations/{id}", { params: { path: { id } } })),
    onSuccess: () => toast.success("Davet iptal edildi."),
    onError: (e) => toast.error(errorMessage(e)),
    onSettled: refresh,
  });

  if (!invitations.data?.length) return null;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">Bekleyen davetler</CardTitle>
      </CardHeader>
      <CardContent>
        <ul className="flex flex-col divide-y divide-border">
          {invitations.data.map((i: Invitation) => {
            const locked = i.role === "Owner" && role !== "Owner";
            return (
              <li key={i.id} className="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:justify-between">
                <div className="flex min-w-0 flex-col gap-1">
                  <span className="truncate text-sm font-semibold text-black">{i.email}</span>
                  <span className="flex flex-wrap items-center gap-2 text-sm text-neutral-600">
                    {roleNames[i.role as Role]}
                    {i.status === "Expired" ? (
                      <Badge className="bg-neutral-100 text-neutral-600">Süresi doldu</Badge>
                    ) : (
                      <Badge className="bg-neutral-100 text-black">Son gün: {formatDate(i.expiresAt)}</Badge>
                    )}
                  </span>
                </div>
                {!locked && (
                  <div className="flex gap-2">
                    <Button variant="outline" disabled={resend.isPending} onClick={() => resend.mutate(i.id)}>
                      <RotateCw aria-hidden className="size-4" /> Yeniden gönder
                    </Button>
                    <Button
                      variant="ghost"
                      className="text-danger-600"
                      aria-label={`${i.email} davetini iptal et`}
                      disabled={revoke.isPending}
                      onClick={() => revoke.mutate(i.id)}
                    >
                      <Trash2 aria-hidden className="size-4" /> İptal
                    </Button>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      </CardContent>
    </Card>
  );
}

function MembersCard() {
  const { role } = useSession();
  const queryClient = useQueryClient();
  const members = useQuery({ queryKey: membersKey, queryFn: () => unwrap(api.GET("/api/team/members")) });
  const [removing, setRemoving] = useState<Member | null>(null);
  const refresh = () => queryClient.invalidateQueries({ queryKey: membersKey });

  const changeRole = useMutation({
    mutationFn: ({ id, role }: { id: string; role: Role }) =>
      unwrap(api.PUT("/api/team/members/{membershipId}/role", { params: { path: { membershipId: id } }, body: { role } })),
    onSuccess: () => toast.success("Rol güncellendi."),
    onError: (e) => toast.error(errorMessage(e)),
    onSettled: refresh,
  });
  const remove = useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/team/members/{membershipId}", { params: { path: { membershipId: id } } })),
    onSuccess: () => {
      toast.success("Üye ekipten çıkarıldı.");
      setRemoving(null);
    },
    onError: (e) => toast.error(errorMessage(e)),
    onSettled: refresh,
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">Ekip</CardTitle>
        <CardDescription>Kimse kendi rolünü değiştiremez. Sahip rolünü sadece işletme sahibi verebilir.</CardDescription>
      </CardHeader>
      <CardContent>
        {members.isPending ? (
          <p role="status" className="text-sm text-neutral-600">Yükleniyor…</p>
        ) : members.isError ? (
          <FormMessage>{errorMessage(members.error)}</FormMessage>
        ) : (
          <ul className="flex flex-col divide-y divide-border">
            {members.data.map((m) => {
              const memberRole = m.role as Role;
              const editable = !m.isYou && (role === "Owner" || memberRole !== "Owner");
              return (
                <li key={m.membershipId} className="flex flex-col gap-3 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="flex min-w-0 flex-col gap-0.5">
                    <span className="flex items-center gap-2 text-sm font-semibold text-black">
                      {m.fullName}
                      {m.isYou && <Badge className="bg-neutral-100 text-black">Sen</Badge>}
                    </span>
                    <span className="truncate text-sm text-neutral-600">{m.email}</span>
                  </div>
                  {editable ? (
                    <div className="flex items-center gap-2">
                      <select
                        aria-label={`${m.fullName} rolü`}
                        className={`${selectClass} sm:w-44`}
                        value={memberRole}
                        disabled={changeRole.isPending}
                        onChange={(e) => changeRole.mutate({ id: m.membershipId, role: e.target.value as Role })}
                      >
                        {assignableRoles(role)
                          .concat(assignableRoles(role).includes(memberRole) ? [] : [memberRole])
                          .map((r) => (
                            <option key={r} value={r}>
                              {roleNames[r]}
                            </option>
                          ))}
                      </select>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="shrink-0 text-danger-600"
                        aria-label={`${m.fullName} kişisini ekipten çıkar`}
                        onClick={() => setRemoving(m)}
                      >
                        <UserMinus aria-hidden className="size-5" />
                      </Button>
                    </div>
                  ) : (
                    <Badge className="self-start bg-black text-white sm:self-center">{roleNames[memberRole]}</Badge>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </CardContent>

      <Dialog open={!!removing} onOpenChange={(open) => !open && setRemoving(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Ekipten çıkarılsın mı?</DialogTitle>
            <DialogDescription>
              {removing?.fullName} bu işletmeye artık erişemez. Gerekirse yeniden davet edebilirsin.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <DialogClose asChild>
              <Button variant="outline">Vazgeç</Button>
            </DialogClose>
            <Button variant="destructive" disabled={remove.isPending} onClick={() => removing && remove.mutate(removing.membershipId)}>
              Ekipten çıkar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  );
}
