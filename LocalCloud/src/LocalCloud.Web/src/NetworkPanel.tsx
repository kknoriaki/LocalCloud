import { useEffect, useState } from "react";
import {
  CheckCircle2,
  AlertCircle,
  Loader2,
  RefreshCw,
  ShieldCheck,
} from "lucide-react";
import { api } from "./api";
interface Check {
  listeningLan: boolean;
  port: number;
  addresses: string[];
  platform: string;
  probes?: {address:string;connected:boolean}[];
  probeScope?:string;
  error?: string;
  windows?: {
    firewallAllowed: boolean;
    profiles: { InterfaceAlias: string; Category: string }[];
  };
}
export function NetworkPanel({ local }: { local: boolean }) {
  const [data, setData] = useState<Check | null>(null),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [message, setMessage] = useState("");
  async function check() {
    setBusy(true);
    setError("");
    try {
      setData(await api<Check>("/network"));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  useEffect(() => {
    if (local) void check();
  }, [local]);
  return (
    <section className="network-check" aria-label="Подключение телефона">
      <h3>Если iPhone не открывает LocalCloud</h3>
      {local && data && (
        <ul className="network-results">
          <li>
            {data.listeningLan ? (
              <CheckCircle2 size={18} />
            ) : (
              <AlertCircle size={18} />
            )}
            <span>
              {data.listeningLan
                ? "Сервер принимает подключения из домашней сети"
                : "Доступ разрешён только с этого компьютера. Включите доступ из домашней сети и перезапустите LocalCloud."}
            </span>
          </li>
          {data.probes?.map(p=><li key={p.address}>{p.connected?<CheckCircle2 size={18}/>:<AlertCircle size={18}/>}<span><a href={p.address} target="_blank" rel="noreferrer">{p.address}</a> — {p.connected?"доступен с ПК":"не отвечает"}</span><button className="text-button" onClick={()=>void navigator.clipboard.writeText(p.address).then(()=>setMessage("Адрес скопирован")).catch(()=>setMessage(p.address))}>Копировать</button></li>)}
          {data.probeScope&&<li><span className="quiet-note">{data.probeScope}</span></li>}
          {data.windows && (
            <>
              <li>
                {data.windows.firewallAllowed ? (
                  <CheckCircle2 size={18} />
                ) : (
                  <AlertCircle size={18} />
                )}
                <span>
                  {data.windows.firewallAllowed
                    ? "Правило LocalCloud в брандмауэре включено"
                    : "Разрешите LocalCloud в брандмауэре Windows кнопкой ниже."}
                </span>
              </li>
              <li>
                {data.windows.profiles.some((p) => p.Category === "Private") ? (
                  <CheckCircle2 size={18} />
                ) : (
                  <AlertCircle size={18} />
                )}
                <span>
                  {data.windows.profiles
                    .map(
                      (p) =>
                        `${p.InterfaceAlias}: ${p.Category === "Private" ? "частная сеть" : p.Category === "Public" ? "общедоступная сеть" : p.Category}`,
                    )
                    .join(" · ") || "Нет подключённой сети"}
                </span>
              </li>
            </>
          )}
          {data.error && (
            <li>
              <AlertCircle size={18} />
              <span>{data.error}</span>
            </li>
          )}
        </ul>
      )}
      <p>
        На iPhone используйте полный адрес{" "}
        <strong>http://IP-компьютера:порт</strong>. Адрес localhost работает
        только на самом компьютере. Если cloud.local не открывается, используйте
        IP из QR-кода.
      </p>
      <p>
        В Windows: Параметры → Сеть и Интернет → свойства домашнего подключения
        → «Частная сеть». Телефон должен быть в той же сети, без гостевого Wi-Fi
        и изоляции устройств. Если используется VPN, временно отключите его для
        проверки.
      </p>
      {local && (
        <div className="network-actions">
          <button
            className="secondary"
            disabled={busy}
            onClick={() => void check()}
          >
            {busy ? (
              <Loader2 size={16} className="spinner" />
            ) : (
              <RefreshCw size={16} />
            )}
            Проверить снова
          </button>
          <button
            className="secondary"
            disabled={busy}
            onClick={async () => {
              setBusy(true);
              setMessage("Перезапускаем установленное приложение…");
              try {
                const r = await api<{ port: number }>("/server/restart", {});
                setTimeout(() => {
                  const target = new URL(location.href);
                  target.port = String(r.port);
                  location.assign(target.href);
                }, 4500);
              } catch (e) {
                setError((e as Error).message);
                setBusy(false);
              }
            }}
          >
            <RefreshCw size={16} />
            Перезапустить сервер
          </button>
          {data?.platform === "windows" && (
            <button
              className="secondary"
              onClick={async () => {
                setMessage("");
                setError("");
                try {
                  const r = await api<{ message: string }>(
                    "/network/allow",
                    {},
                  );
                  setMessage(r.message);
                } catch (e) {
                  setError((e as Error).message);
                }
              }}
            >
              <ShieldCheck size={16} />
              Разрешить в Windows
            </button>
          )}
        </div>
      )}
      {(error || message) && <p role="status">{error || message}</p>}
    </section>
  );
}
