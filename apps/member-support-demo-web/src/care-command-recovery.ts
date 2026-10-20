// TRN-TECH-189. Portable, manually copied synthetic-only care command.
// A receipt is neither signed evidence nor permission to perform care.
// On import there is NO network request or automatic replay.
export type CareSurface='MEMBER'|'CENTER';
export type CareCommand={
 requestId:string;expectedRevision:number;
 action:'REQUEST_NONCLINICAL_COORDINATION'|
  'RECORD_UNVERIFIED_COORDINATOR_CONTACT'|'ATTACH_UNVERIFIED_CLINICAL_SOURCE';
 memberRef:string;originalMemberRequestRef:string;originalSubmissionRequestId:string;
 claimedActorRef:string|null;unverifiedNoteOrDocumentRef:string|null;
};
export type CareReceipt={caseRef:string;body:string;command:CareCommand};
const contract='TRN-TECH-189';
const zero='00000000-0000-0000-0000-000000000000';
const fields=['requestId','expectedRevision','action','memberRef',
 'originalMemberRequestRef','originalSubmissionRequestId','claimedActorRef',
 'unverifiedNoteOrDocumentRef'] as const;
const ref=(s:unknown):s is string=>typeof s==='string'&&s.length<=100&&
 /^test:[A-Za-z0-9_:-]+$/.test(s);
const uuid=(s:unknown):s is string=>typeof s==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(s);
const exact=(v:unknown,names:readonly string[]):v is Record<string,unknown>=>
 !!v&&typeof v==='object'&&!Array.isArray(v)&&
 Object.keys(v).length===names.length&&names.every(k=>Object.hasOwn(v,k));
function commandValid(value:unknown,surface:CareSurface):value is CareCommand{
 if(!exact(value,fields))return false;
 const c=value;
 if(!uuid(c.requestId)||!Number.isSafeInteger(c.expectedRevision)||
  (c.expectedRevision as number)<0||
  (c.expectedRevision as number)>=Number.MAX_SAFE_INTEGER||
  !ref(c.memberRef)||!ref(c.originalMemberRequestRef))return false;
 if(c.action==='REQUEST_NONCLINICAL_COORDINATION')
  return surface==='MEMBER'&&c.expectedRevision===0&&
   c.originalSubmissionRequestId===zero&&c.claimedActorRef===null&&
   c.unverifiedNoteOrDocumentRef===null;
 if(c.action!=='RECORD_UNVERIFIED_COORDINATOR_CONTACT'&&
    c.action!=='ATTACH_UNVERIFIED_CLINICAL_SOURCE')return false;
 return (c.expectedRevision as number)>=1&&
  uuid(c.originalSubmissionRequestId)&&
  (c.originalSubmissionRequestId as string).toLowerCase()!==
   (c.requestId as string).toLowerCase()&&
  ref(c.claimedActorRef)&&ref(c.unverifiedNoteOrDocumentRef);
}
export function parseCareReceipt(text:string,surface:CareSurface):CareReceipt|null{
 if(typeof text!=='string'||text.length<100||text.length>4096)return null;
 try{
  const v:unknown=JSON.parse(text);
  if(!exact(v,['contract','syntheticOnly','surface','caseRef','body'])||
   JSON.stringify(v)!==text||v.contract!==contract||
   v.syntheticOnly!==true||v.surface!==surface||!ref(v.caseRef)||
   typeof v.body!=='string'||v.body.length>1400)return null;
  const cmd:unknown=JSON.parse(v.body);
  if(JSON.stringify(cmd)!==v.body||!commandValid(cmd,surface))return null;
  return {caseRef:v.caseRef,body:v.body,command:cmd};
 }catch{return null;}
}
export function careReceipt(s:CareSurface,caseRef:string,body:string):string{
 const output=JSON.stringify({contract,syntheticOnly:true,surface:s,caseRef,body});
 if(!parseCareReceipt(output,s))throw Error('INVALID_SYNTHETIC_CARE_COMMAND');
 return output;
}
