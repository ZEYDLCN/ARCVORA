import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { LogoMark, logoMaskDataUri } from './Logo';
import { markIntroSeen, safeSessionStorage } from '../lib/intro';
import '../intro.css';

/** Animasyon olayı gelmezse (ör. sekme arka planda duruyorsa) katmanın yine de kalkmasını sağlayan güvenlik süresi. */
const FALLBACK_MS = 4500;

type Phase = 'play' | 'leave' | 'skip';

/**
 * Açılış animasyonu. Uygulama altında zaten yüklenmiştir; katman yalnızca üstte oynar, hiçbir şeyi engellemez:
 * tıklama veya herhangi bir tuş animasyonu hemen kapatır. Bitiş, zamanlayıcıya değil CSS `animationend` olayına bağlıdır;
 * böylece görsel zaman çizelgesi ile uygulama devri hiçbir zaman ayrışmaz.
 */
export function Intro({ onDone }: { onDone: () => void }) {
  const [phase, setPhase] = useState<Phase>('play');
  const phaseRef = useRef<Phase>('play');
  const finished = useRef(false);
  const mask = useMemo(() => `url("${logoMaskDataUri()}")`, []);

  // Üst bileşen her render'da yeni bir onDone verebilir; efektlerin (ve `data-intro` işaretinin) yeniden kurulmaması için ref
  const doneRef = useRef(onDone);
  doneRef.current = onDone;
  const finish = useCallback(() => {
    if (finished.current) return;
    finished.current = true;
    doneRef.current();
  }, []);

  const markLeaving = useCallback(() => {
    document.documentElement.dataset.intro = 'leaving';
  }, []);

  const skip = useCallback(() => {
    if (phaseRef.current !== 'play') return;     // çıkış zaten başladıysa yeniden başlatma (titreme olmasın)
    phaseRef.current = 'skip';
    setPhase('skip');
    markLeaving();
  }, [markLeaving]);

  useEffect(() => {
    const root = document.documentElement;
    markIntroSeen(safeSessionStorage());
    root.classList.remove('intro-boot');            // React geldi: boot zemini artık katmanın kendi arka planı
    const onKey = () => skip();
    window.addEventListener('keydown', onKey);
    const fallback = window.setTimeout(finish, FALLBACK_MS);
    return () => {
      window.removeEventListener('keydown', onKey);
      window.clearTimeout(fallback);
      delete root.dataset.intro;
    };
  }, [skip, finish]);

  const isExit = (e: React.AnimationEvent) => e.target === e.currentTarget && (e.animationName === 'intro-exit' || e.animationName === 'intro-skip');

  return (
    <div className="intro" data-phase={phase} aria-hidden="true" role="presentation"
      onPointerDown={skip}
      onAnimationStart={(e) => {
        if (!isExit(e)) return;
        if (phaseRef.current === 'play') phaseRef.current = 'leave';
        setPhase((p) => (p === 'play' ? 'leave' : p));
        markLeaving();
      }}
      onAnimationEnd={(e) => { if (isExit(e)) finish(); }}>
      <div className="intro-stage">
        <div className="intro-glow l" />
        <div className="intro-glow a" />
        <div className="intro-in">
          <div className="intro-rot">
            <div className="intro-halo"><LogoMark size={128} /></div>
            <LogoMark size={128} />
            <div className="intro-sheen-mask" style={{ maskImage: mask, WebkitMaskImage: mask }}><div className="intro-sheen" /></div>
          </div>
        </div>
        <div className="intro-brand">
          <div className="intro-word">Arcvora</div>
          <div className="intro-tag" lang="en">Turn intent into architecture</div>
          <div className="intro-bar"><i /></div>
        </div>
      </div>
    </div>
  );
}
