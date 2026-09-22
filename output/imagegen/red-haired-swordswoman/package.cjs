const sharp = require('/Users/hyun_woo/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const fs = require('node:fs/promises');
const path = require('node:path');

async function main() {
  const input = process.argv[2];
  const out = __dirname;
  const meta = await sharp(input).metadata();
  await fs.mkdir(path.join(out, 'frames'), {recursive:true});
  const frames = [];
  for (let i=0;i<8;i++) {
    const col=i%4, row=Math.floor(i/4);
    const left=Math.round(col*meta.width/4), top=Math.round(row*meta.height/2);
    const width=Math.round((col+1)*meta.width/4)-left;
    const height=Math.round((row+1)*meta.height/2)-top;
    const frame=await sharp(input).extract({left,top,width,height})
      .resize(64,64,{kernel:'nearest',fit:'fill'}).png().toBuffer();
    frames.push(frame);
    await fs.writeFile(path.join(out,'frames',`idle-${String(i+1).padStart(2,'0')}.png`),frame);
  }
  await sharp({create:{width:512,height:64,channels:4,background:'#00000000'}})
    .composite(frames.map((input,i)=>({input,left:i*64,top:0})))
    .png().toFile(path.join(out,'idle-sheet-512x64.png'));
  const raw=await Promise.all(frames.map(f=>sharp(f).ensureAlpha().raw().toBuffer()));
  await sharp(Buffer.concat(raw),{raw:{width:64,height:512,channels:4,pageHeight:64}})
    .gif({loop:0,delay:Array(8).fill(100),colours:64,dither:0,keepDuplicateFrames:true})
    .toFile(path.join(out,'idle-64x64.gif'));
  const previews=await Promise.all(frames.map(f=>sharp(f).resize(256,256,{kernel:'nearest'}).ensureAlpha().raw().toBuffer()));
  await sharp(Buffer.concat(previews),{raw:{width:256,height:2048,channels:4,pageHeight:256}})
    .gif({loop:0,delay:Array(8).fill(100),colours:64,dither:0,keepDuplicateFrames:true})
    .toFile(path.join(out,'preview-4x.gif'));
  await sharp(path.join(out,'idle-sheet-512x64.png')).resize(1536,192,{kernel:'nearest'})
    .png().toFile(path.join(out,'sheet-preview-3x.png'));
  console.log(JSON.stringify(await sharp(path.join(out,'idle-64x64.gif'),{animated:true}).metadata(),null,2));
}
main().catch(e=>{console.error(e);process.exitCode=1;});
