import { useEffect, useRef, useState } from 'react';
import { Play, Pause, Volume2, VolumeX, Maximize, Music2, Loader2, RotateCcw } from 'lucide-react';
import { duration,thumbnailUrl,type FileEntry } from './api';
export function MediaPlayer({file,src,poster,autoPlay=false,onError}:{file:FileEntry;src:string;poster?:string;autoPlay?:boolean;onError:()=>void}) {
 const ref=useRef<HTMLMediaElement|null>(null),root=useRef<HTMLDivElement>(null);
 const [playing,setPlaying]=useState(false),[time,setTime]=useState(0),[total,setTotal]=useState(file.duration||0),[volume,setVolume]=useState(1),[muted,setMuted]=useState(false),[loading,setLoading]=useState(false),[failed,setFailed]=useState(false);
 const audio=file.kind==='audio';let tags:Record<string,string>={};try{tags=JSON.parse(file.metadata||'{}');}catch{}
 const title=tags.title||tags.TITLE||file.name,artist=tags.artist||tags.ARTIST;
 useEffect(()=>{setTime(0);setTotal(file.duration||0);setPlaying(false);setFailed(false);setLoading(false);},[src,file.id,file.duration]);
 function play(){const m=ref.current;if(!m)return;if(m.paused){setFailed(false);void m.play().catch(()=>{setFailed(true);setLoading(false);});}else m.pause();}
 function seek(value:number){const m=ref.current;if(m&&Number.isFinite(value)){m.currentTime=value;setTime(value);}}
 async function fullscreen(){const v=ref.current as HTMLVideoElement&{webkitEnterFullscreen?:()=>void};try{if(v?.webkitEnterFullscreen)v.webkitEnterFullscreen();else if(document.fullscreenElement)await document.exitFullscreen();else await root.current?.requestFullscreen();}catch{/* Native video controls remain available from the context menu. */}}
 const events={onPlay:()=>setPlaying(true),onPause:()=>setPlaying(false),onEnded:()=>{setPlaying(false);setLoading(false);},onTimeUpdate:()=>setTime(ref.current?.currentTime||0),onLoadedMetadata:()=>{const d=ref.current?.duration;if(d&&Number.isFinite(d))setTotal(d);},onWaiting:()=>setLoading(true),onPlaying:()=>setLoading(false),onCanPlay:()=>setLoading(false),onError:()=>{setLoading(false);setFailed(true);onError();}};
 return <div ref={root} className={'media-player '+(audio?'audio-player':'video-player')} tabIndex={0} aria-label={audio?'Аудиоплеер':'Видеоплеер'} onKeyDown={e=>{if((e.target as HTMLElement).closest('input,button'))return;if(e.code==='Space'){e.preventDefault();e.stopPropagation();play();}if(e.key==='ArrowRight'||e.key==='ArrowLeft'){e.preventDefault();e.stopPropagation();seek(Math.max(0,Math.min(total,time+(e.key==='ArrowRight'?10:-10))));}}}>
  {audio?<><div className="audio-art"><Music2 size={66}/>{tags.cover&&<img src={thumbnailUrl(file,512)} alt="Обложка" onError={e=>{e.currentTarget.style.display='none';}}/>}</div><h2>{title}</h2><p>{artist||'Аудиофайл в вашей библиотеке'}{tags.album?' · '+tags.album:''}</p><audio key={src} ref={e=>{ref.current=e;}} src={src} preload="metadata" {...events}/></>:<video key={src} ref={e=>{ref.current=e;}} playsInline autoPlay={autoPlay} poster={poster} src={src} preload="metadata" onClick={()=>{root.current?.focus();play();}} {...events}/>}
  {!audio&&!playing&&<button className="media-play-overlay" onClick={play} aria-label="Воспроизвести видео">{loading?<Loader2 className="spinner" size={30}/>:<Play size={30} fill="currentColor"/>}</button>}
  <div className="media-controls" onTouchStart={e=>e.stopPropagation()} onTouchEnd={e=>e.stopPropagation()}>
   <input className="media-timeline" type="range" aria-label="Позиция воспроизведения" min={0} max={Math.max(total,1)} step={0.1} value={Math.min(time,total||1)} disabled={!total} onChange={e=>seek(Number(e.target.value))}/>
   <div className="media-control-row"><button aria-label={playing?'Приостановить воспроизведение':'Воспроизвести'} onClick={play}>{loading?<Loader2 className="spinner" size={21}/>:playing?<Pause size={21}/>:<Play size={21}/>}</button>
    <button aria-label="Назад на 10 секунд" onClick={()=>seek(Math.max(0,time-10))}><RotateCcw size={19}/></button><span className="media-time">{duration(time)} <small>/ {duration(total)}</small></span>
    <div className="media-volume"><button aria-label={muted?'Включить звук':'Выключить звук'} onClick={()=>{if(ref.current)ref.current.muted=!muted;setMuted(!muted);}}>{muted||volume===0?<VolumeX size={20}/>:<Volume2 size={20}/>}</button><input type="range" aria-label="Громкость" min={0} max={1} step={0.05} value={muted?0:volume} onChange={e=>{const v=Number(e.target.value);setVolume(v);setMuted(false);if(ref.current){ref.current.volume=v;ref.current.muted=false;}}}/></div>
    {!audio&&<button aria-label="На весь экран" onClick={()=>void fullscreen()}><Maximize size={20}/></button>}
   </div>
  </div>
  {failed&&<p className="media-play-error" role="status">Не удалось начать воспроизведение. Попробуйте ещё раз или скачайте оригинал.</p>}
 </div>;
}
