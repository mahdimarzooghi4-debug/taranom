import {PatientSentInvitationActivity} from './patient-sent-invitation-activity';
import {PatientInvitationHistory} from './patient-invitation-history';
import {PatientGroupInvitation} from './patient-group-invitation';
import {PatientGroupDrafts} from './patient-group-drafts';
import {PatientAllServices} from './patient-all-services';
import {PatientHome} from './patient-home';
import {PatientNotifications} from './patient-notifications';
import { PatientInstall } from './patient-install';
import { PatientAccount } from './patient-account';
import { AppointmentDiscovery } from './appointment-discovery';
import { PatientSupport } from './patient-support';
import { PatientProfile } from './patient-profile';
import { FigmaBooking } from './figma-booking';
import { FigmaAppointments } from './figma-appointments';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MemberApp } from './member-app';
import './styles.css';
import { PatientWebPortal } from './patient-web-portal';
import './patient-web-portal.css';
import { PatientMobileApp } from './patient-mobile-app';
import './patient-mobile-app.css';

const node = document.getElementById('root');
if (!node) throw new Error('Member application root unavailable');
createRoot(node).render(
  <StrictMode>
    {window.location.pathname==='/patient-app/invitations-sent' ? <PatientSentInvitationActivity/> : window.location.pathname==='/patient-app/invitation-history' ? <PatientInvitationHistory/> : ['/patient-app/group-invitation','/patient-app/join-group'].includes(window.location.pathname) ? <PatientGroupInvitation receiving={window.location.pathname.endsWith('/join-group')}/> : ['/patient-app/group','/patient-app/group-members','/patient-app/add-member'].includes(window.location.pathname) ? <PatientGroupDrafts adding={window.location.pathname.endsWith('/add-member')}/> : window.location.pathname==='/patient-app/services' ? <PatientAllServices/> : ['/patient-app','/patient-app/'].includes(window.location.pathname) ? <PatientHome/> : window.location.pathname==='/patient-app/notifications' ? <PatientNotifications/> : window.location.pathname==='/patient-app/install' ? <PatientInstall/> : ['/patient-app/booking','/patient-app/providers'].includes(window.location.pathname) ? <AppointmentDiscovery/> : window.location.pathname==='/patient-app/account' ? <PatientAccount/> : window.location.pathname==='/patient-app/support' ? <PatientSupport/> : ['/patient-app/personal-info','/patient-app/edit-info'].includes(window.location.pathname) ? <PatientProfile editing={window.location.pathname.endsWith('/edit-info')}/> : ['/patient-app/slots','/patient-app/review'].includes(window.location.pathname) ? <FigmaBooking/> : ['/patient-app/appointments','/patient-app/appointment-detail','/patient-app/request-tracking'].includes(window.location.pathname) ? <FigmaAppointments/> : window.location.pathname === '/patient-app' || window.location.pathname.startsWith('/patient-app/')
      ? <PatientMobileApp />
      : window.location.pathname === '/patient-portal' || window.location.pathname.startsWith('/patient-portal/')
      ? <PatientWebPortal />
      : <MemberApp />}
  </StrictMode>
);
